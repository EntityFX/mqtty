using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Counter;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Scenarios;
using Microsoft.Extensions.Configuration;

namespace EntityFX.MqttY.Network;

public class NetworkGraph : INetworkSimulator
{
    private readonly IServiceProvider serviceProvider;
    private readonly INetworkBuilder _networkBuilder;
    private readonly ConcurrentDictionary<(string Address, NodeType NodeType), ILeafNode> _nodes = new();
    private readonly ConcurrentDictionary<string, INetwork> _networks = new();

    private long _tick = 0;

    private CancellationTokenSource? cancelTokenSource;

    internal Exception? SimulationException;

    public event EventHandler<Exception>? OnError;

    public event EventHandler<long>? OnRefresh;

    private readonly NetworkSimulatorCounters counters;

    public CounterGroup Counters => counters;

    private Timer? _timer;

    public NetworkGraph(
        IServiceProvider serviceProvider,
        INetworkBuilder networkBuilder,
        IPathFinder pathFinder,
        INetworkLogger monitoring, TicksOptions ticksOptions)
    {
        this.serviceProvider = serviceProvider;
        _networkBuilder = networkBuilder;
        PathFinder = pathFinder;
        Monitoring = monitoring;
        PathFinder.NetworkGraph = this;

        counters = new NetworkSimulatorCounters("NetworkSimulator", ticksOptions);
    }

    public string? OptionsPath { get; set; }

    public IPathFinder PathFinder { get; }

    public INetworkLogger Monitoring { get; }

    public IImmutableDictionary<string, INetwork> Networks => _networks.ToImmutableDictionary();

    public IImmutableDictionary<string, IClient> Clients => _nodes.Where(n => n.Key.NodeType == NodeType.Client)
        .ToDictionary(k => k.Key.Address, v => (IClient)v.Value).ToImmutableDictionary();
    public IImmutableDictionary<string, IServer> Servers => _nodes.Where(n => n.Key.NodeType == NodeType.Server)
        .ToDictionary(k => k.Key.Address, v => (IServer)v.Value).ToImmutableDictionary();

    public long TotalTicks => _tick;

    public string GetAddress(string name, string protocolType, string networkAddress)
    {
        return $"{protocolType}://{name}.{networkAddress}";
    }

    public INetwork? GetNetworkByNode(string address, NodeType nodeType)
    {
        return GetNode(address, nodeType)?.Network;
    }

    public ILeafNode? GetNode(string address, NodeType nodeType)
    {
        if (!_nodes.ContainsKey((address, nodeType)))
        {
            return null;
        }

        return _nodes[(address, nodeType)];
    }

    public Contracts.Network.NetworkPacket GetReversePacket(Contracts.Network.NetworkPacket packet, byte[] payload, string? category)
    {
        return new Contracts.Network.NetworkPacket(
            To: packet.From,
            From: packet.To,
            Payload: payload,
            FromType: packet.ToType,
            ToType: packet.FromType,
            Protocol: packet.Protocol,
            Category: category ?? packet.Category,
            Scope: packet.Scope
        )
        {
            Id = Guid.NewGuid(),
            RequestId = packet.Id
        };
    }

    public void RemoveClient(string clientAddress)
    {
        if (!_nodes.ContainsKey((clientAddress, NodeType.Client)))
        {
            return;
        }

        var client = GetNode(clientAddress, NodeType.Client) as IClient;
        if (client == null)
        {
            return;
        }

        client.Disconnect();

        _nodes.Remove((clientAddress, NodeType.Client), out _);
    }

    public void RemoveNetwork(string networkAddress)
    {
        if (_networks.ContainsKey(networkAddress))
        {
            return;
        }

        var network = _networks.GetValueOrDefault(networkAddress);
        if (network == null)
        {
            return;
        }

        network.UnlinkAll();

        _networks.Remove(networkAddress, out _);

        UpdateRoutes();
    }

    public void RemoveServer(string serverAddress)
    {
        if (!_nodes.ContainsKey((serverAddress, NodeType.Server)))
        {
            return;
        }

        var server = GetNode(serverAddress, NodeType.Client) as IServer;
        if (server == null)
        {
            return;
        }

        server.Stop();

        _nodes.Remove((serverAddress, NodeType.Client), out _);
    }

    public async Task<bool> Refresh()
    {
        try
        {
            var scope = Monitoring.BeginScope(TotalTicks, "Refresh sourceNetwork graph");
            Monitoring.Push(TotalTicks, NetworkLoggerType.Refresh, $"Refresh whole sourceNetwork", "Network", "Refresh", scope);
            Tick();
            var bytes = Array.Empty<byte>();

            counters.Refresh(TotalTicks);

            var networksRefresh = new List<Task>();
            foreach (var network in _networks)
            {
                Monitoring.Push(TotalTicks,
                    network.Value, network.Value, bytes, NetworkLoggerType.Refresh, $"Refresh sourceNetwork {network.Key}",
                    "Network", "Refresh", scope);
                networksRefresh.Add(network.Value.Refresh());
            }

            Task.WaitAll(networksRefresh.ToArray());

            foreach (var node in _nodes)
            {
                Monitoring.Push(TotalTicks,
                    node.Value, node.Value, bytes, NetworkLoggerType.Refresh, $"Refresh node {node.Key}", "Network", "Refresh",
                    scope);
                await node.Value.Refresh();
            }

            Monitoring.EndScope(TotalTicks, scope);

            return true;
        }
        catch (Exception ex)
        {
            SimulationException = ex;
            return false;
        }
    }

    public bool Reset()
    {
        try
        {
            Tick();
            var scope = Monitoring.BeginScope(TotalTicks, "Reset sourceNetwork graph");
            Monitoring.Push(TotalTicks, NetworkLoggerType.Refresh, $"Reset whole sourceNetwork", "Network", "Reset", scope);

            var bytes = Array.Empty<byte>();

            foreach (var network in _networks)
            {
                Monitoring.Push(TotalTicks,
                    network.Value, network.Value, bytes, NetworkLoggerType.Refresh, $"Reset sourceNetwork {network.Key}",
                    "Network", "Refresh", scope);
                network.Value.Reset();
            }

            foreach (var node in _nodes)
            {
                Monitoring.Push(TotalTicks,
                    node.Value, node.Value, bytes, NetworkLoggerType.Refresh, $"RefrResetesh node {node.Key}", "Network", "Reset",
                    scope);
                node.Value.Reset();
            }

            Monitoring.EndScope(TotalTicks, scope);

            return true;
        }
        catch (Exception ex)
        {
            SimulationException = ex;
            return false;
        }
    }

    public Task StartPeriodicRefreshAsync()
    {
        if (cancelTokenSource != null && cancelTokenSource.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        cancelTokenSource = new CancellationTokenSource();

        _timer = new Timer(Refreshed, this, 0, 1000);

        return Task.Run(async () =>
        {
            bool refreshResult = true;

            while (true)
            {
                if (cancelTokenSource.Token.IsCancellationRequested)
                {
                    return;
                }

                refreshResult = await Refresh();

                if (!refreshResult)
                {
                    Reset();

                    cancelTokenSource.Cancel();

                    OnError?.Invoke(this, SimulationException!);
                    break;
                }
            }
        }, cancelTokenSource.Token);
    }

    private void Refreshed(object? state)
    {
        OnRefresh?.Invoke(this, TotalTicks);
    }

    public void Tick()
    {
        Interlocked.Increment(ref _tick);
    }


    public void StopPeriodicRefresh()
    {
        _timer?.Change(0, 0);
        _timer?.Dispose();
        cancelTokenSource?.Cancel();
    }

    public bool AddClient(IClient client)
    {
        if (_nodes.ContainsKey((client.Name, NodeType.Client)))
        {
            return false;
        }

        return _nodes.TryAdd((client.Name, NodeType.Client), client);
    }

    public bool AddServer(IServer server)
    {
        if (_nodes.ContainsKey((server.Name, NodeType.Server)))
        {
            return false;
        }

        return _nodes.TryAdd((server.Name, NodeType.Server), server);
    }

    public bool AddApplication(IApplication application)
    {
        if (_nodes.ContainsKey((application.Name, NodeType.Application)))
        {
            return false;
        }

        return _nodes.TryAdd((application.Name, NodeType.Application), application);
    }

    public bool AddNetwork(INetwork network)
    {
        if (_networks.ContainsKey(network.Name))
        {
            return false;
        }

        var result = _networks.TryAdd(network.Name, network);

        if (!result)
        {
            return false;
        }

        UpdateRoutes();

        return result;
    }

    public void UpdateRoutes()
    {
        PathFinder.Build();
        counters.WithNetworks(_networks.Values);
    }

    public bool Link(string sourceNetwork, string destinationNetwork)
    {
        var source = _networks.GetValueOrDefault(sourceNetwork);
        if (source == null)
        {
            return false;
        }

        var destination = _networks.GetValueOrDefault(destinationNetwork);
        if (destination == null)
        {
            return false;
        }

        return source.Link(destination);
    }
}