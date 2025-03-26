using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using Microsoft.Extensions.Configuration;

namespace EntityFX.MqttY.Network;

public class NetworkGraph : INetworkGraph
{
    private readonly INetworkBuilder _networkBuilder;
    private readonly ConcurrentDictionary<(string Address, NodeType NodeType), ILeafNode> _nodes = new();
    private readonly ConcurrentDictionary<string, INetwork> _networks = new();

    private CancellationTokenSource? cancelTokenSource;

    public NetworkGraph(
        INetworkBuilder networkBuilder,
        IPathFinder pathFinder,
        IMonitoring monitoring)
    {
        _networkBuilder = networkBuilder;
        PathFinder = pathFinder;
        Monitoring = monitoring;
        PathFinder.NetworkGraph = this;
    }

    public string? OptionsPath { get; set; }

    public IPathFinder PathFinder { get; }

    public IMonitoring Monitoring { get; }

    public IImmutableDictionary<string, INetwork> Networks => _networks.ToImmutableDictionary();

    public IClient? BuildClient(int index, string name, string protocolType, string specification,
        INetwork network, string? group = null, int? groupAmount = null,
        Dictionary<string, string[]>? additional = default)
    {
        if (_nodes.ContainsKey((name, NodeType.Client)))
        {
            return null;
        }

        var clientAddressFull = GetAddress(name, protocolType, network.Address);

        var client = _networkBuilder
            .ClientFactory.Create(
                new NodeBuildOptions<Dictionary<string, string[]>>(
                    this, network, index, name, clientAddressFull, group, groupAmount, protocolType,
                    specification, null, additional));

        if (client == null)
        {
            return null;
        }

        _nodes.TryAdd((name, NodeType.Client), client);

        return client;
    }

    public TClient? BuildClient<TClient>(int index, string name, string protocolType, string specification,
        INetwork network,
        string? group = null, int? groupAmount = null, Dictionary<string, string[]>? additional = default)
        where TClient : IClient
    {
        return (TClient?)BuildClient(index, name, protocolType, specification, network, group, groupAmount, additional);
    }

    public INetwork? BuildNetwork(int index, string name, string address, TicksOptions ticks)
    {
        if (_networks.ContainsKey(address))
        {
            return null;
        }

        var network = _networkBuilder
            .NetworkFactory.Create(
                new NodeBuildOptions<(TicksOptions TicksOptions, Dictionary<string, string[]> Additional)>(
                    this, null, index, name, address, null, null, "IP",
                    String.Empty, null, (ticks, new Dictionary<string, string[]>())));

        if (network == null)
        {
            return null;
        }

        _networks.TryAdd(name, network);

        return network;
    }

    public ILeafNode? BuildNode(int index, string name, string address, NodeType nodeType, string? group = null,
        int? groupAmount = null,
        Dictionary<string, string[]>? additional = null)
    {
        return null;
    }

    public IServer? BuildServer(int index, string name, string protocolType, string specification,
        INetwork network,
        string? group = null, int? groupAmount = null, Dictionary<string, string[]>? additional = null)
    {
        if (_nodes.ContainsKey((name, NodeType.Server)))
        {
            return null;
        }

        var serverAddressFull = GetAddress(name, protocolType, network.Address);

        var server = _networkBuilder
            .ServerFactory.Create(
                new NodeBuildOptions<Dictionary<string, string[]>>(this, network, index, name, serverAddressFull, group,
                    groupAmount, protocolType,
                    specification, null, additional));

        if (server == null)
        {
            return null;
        }

        _nodes.TryAdd((name, NodeType.Server), server);

        return server;
    }

    public void Configure(NetworkGraphOption option)
    {
        if (option.Networks.Any() != true)
        {
            return;
        }

        foreach (var networkOption in option.Networks)
        {
            BuildNetwork(networkOption.Value.Index, networkOption.Key, networkOption.Key, option.Ticks);
        }

        ConfigureLinks(option);

        PathFinder.Build();

        BuildNodes(option);

        ConfigureNodes(option);
    }

    private void ConfigureNodes(NetworkGraphOption option)
    {
        ConfigureServers(option);
        ConfigureClients(option);
        ConfigureApplications(option);
    }

    private void ConfigureApplications(NetworkGraphOption option)
    {
        var applications = option.Nodes.Where(nt => nt.Value.Type == NodeOptionType.Application).ToArray();
        foreach (var application in applications)
        {
            var nodeApplication = GetNode(application.Key, NodeType.Application) as IApplication;
            if (nodeApplication == null) continue;

            var bo = new NodeBuildOptions<object>(
                this, nodeApplication.Network, nodeApplication.Index, nodeApplication.Name, nodeApplication.Address,
                nodeApplication.Group, nodeApplication.GroupAmount, nodeApplication.ProtocolType,
                nodeApplication.Specification ?? string.Empty,
                null, application.Value.Configuration);

            _networkBuilder.ApplicationFactory.Configure(bo, nodeApplication);
        }
    }

    private void ConfigureServers(NetworkGraphOption option)
    {
        var servers = option.Nodes.Where(nt => nt.Value.Type == NodeOptionType.Server).ToArray();
        foreach (var node in servers)
        {
            var nodeServer = GetNode(node.Key, NodeType.Server) as IServer;
            if (nodeServer == null) continue;

            var bo = new NodeBuildOptions<Dictionary<string, string[]>>(
                this, nodeServer.Network, nodeServer.Index, nodeServer.Name, nodeServer.Address,
                nodeServer.Group, nodeServer.GroupAmount, nodeServer.ProtocolType,
                nodeServer.Specification, node.Value.ConnectsToServer, node.Value.Additional);

            _networkBuilder.ServerFactory.Configure(bo, nodeServer);
        }
    }

    private void ConfigureClients(NetworkGraphOption option)
    {
        var clients = option.Nodes.Where(nt => nt.Value.Type == NodeOptionType.Client).ToArray();
        foreach (var node in clients)
        {
            if (node.Value.Quantity > 1)
            {
                Enumerable.Range(1, node.Value.Quantity.Value).ToList()
                    .ForEach(
                        (nc) =>
                        {
                            var nodeClient = GetNode($"{node.Key}_{nc}", NodeType.Client) as IClient;

                            if (nodeClient == null)
                            {
                                return;
                            }

                            var bo = new NodeBuildOptions<Dictionary<string, string[]>>(
                                this, nodeClient.Network, nodeClient.Index, nodeClient.Name, nodeClient.Address,
                                nodeClient.Group, nodeClient.GroupAmount, nodeClient.ProtocolType,
                                node.Value.Specification ?? string.Empty,
                                node.Value.ConnectsToServer, node.Value.Additional);

                            _networkBuilder.ClientFactory.Configure(bo, nodeClient);
                        });
            }
            else
            {
                var nodeClient = GetNode(node.Key, NodeType.Client) as IClient;
                if (nodeClient == null) continue;

                var bo = new NodeBuildOptions<Dictionary<string, string[]>>(
                    this, nodeClient.Network, nodeClient.Index, nodeClient.Name, nodeClient.Address,
                    nodeClient.Group, nodeClient.GroupAmount, nodeClient.ProtocolType,
                    node.Value.Specification ?? string.Empty, node.Value.ConnectsToServer, node.Value.Additional);
                _networkBuilder.ClientFactory.Configure(bo, nodeClient);
            }
        }
    }

    private void BuildNodes(NetworkGraphOption option)
    {
        var index = 0;
        foreach (var node in option.Nodes)
        {
            var linkNetwork = _networks.GetValueOrDefault(node.Value.Network ?? string.Empty);
            if (linkNetwork == null) continue;

            switch (node.Value.Type)
            {
                case NodeOptionType.Server:

                    BuildServer(index, node.Key, node.Value.Protocol ?? "tcp",
                        node.Value.Specification ?? "tcp-server",
                        linkNetwork, null, null, node.Value.Additional);
                    break;

                case NodeOptionType.Client:

                    if (node.Value.Quantity > 1)
                    {
                        Enumerable.Range(1, node.Value.Quantity.Value).ToList()
                            .ForEach(
                                (nc) => BuildClient(
                                    index, $"{node.Key}_{nc}",
                                    node.Value.Protocol ?? "tcp",
                                    node.Value.Specification ?? "tcp-client",
                                    linkNetwork, node.Key, node.Value.Quantity, node.Value.Additional));
                    }
                    else
                    {
                        BuildClient(index, node.Key, node.Value.Protocol ?? "tcp",
                            node.Value.Specification ?? "tcp-client",
                            linkNetwork, null, null, node.Value.Additional);
                    }

                    break;
                case NodeOptionType.Application:
                    BuildApplication(index, node.Key, node.Value.Protocol ?? "tcp",
                        node.Value.Specification ?? "tcp-app",
                        linkNetwork, null, null, node.Value.Configuration);
                    break;
            }

            index++;
        }
    }

    private void ConfigureLinks(NetworkGraphOption option)
    {
        var scope = Monitoring.BeginScope("Configure sourceNetwork links");
        foreach (var networkOption in option.Networks)
        {
            if (networkOption.Value?.Links?.Any() != true) continue;

            foreach (var link in networkOption.Value.Links)
            {
                if (link?.Network == null || !_networks.ContainsKey(link.Network)) continue;

                var sourceNetwork = _networks[networkOption.Key];
                var destinationNetwork = _networks[link.Network];
                sourceNetwork.Scope = scope;
                destinationNetwork.Scope = scope;
                sourceNetwork.Link(destinationNetwork);
                sourceNetwork.Scope = null;
                destinationNetwork.Scope = null;
            }
        }

        Monitoring.EndScope(scope);
    }

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

    public Packet GetReversePacket(Packet packet, byte[] payload, string? category)
    {
        return new Packet(
            To: packet.From,
            From: packet.To,
            Payload: payload,
            FromType: packet.ToType,
            ToType: packet.FromType,
            Protocol: packet.Protocol,
            Category: category ?? packet.Category,
            Scope: packet.Scope
        );
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

    public void Refresh()
    {

        var scope = Monitoring.BeginScope("Refresh sourceNetwork graph");
        Monitoring.Push(MonitoringType.Refresh, $"Refresh whole sourceNetwork", "Network", "Refresh", scope);
        Tick();
        var bytes = Array.Empty<byte>();

        foreach (var network in _networks)
        {
            Monitoring.Push(
                network.Value, network.Value, bytes, MonitoringType.Refresh, $"Refresh sourceNetwork {network.Key}",
                "Network", "Refresh", scope);
            network.Value.Refresh();
        }

        foreach (var node in _nodes)
        {
            Monitoring.Push(
                node.Value, node.Value, bytes, MonitoringType.Refresh, $"Refresh node {node.Key}", "Network", "Refresh",
                scope);
            node.Value.Refresh();
        }

        Monitoring.EndScope(scope);
    }

    public Task StartPeriodicRefreshAsync()
    {
        //var ticksForRefresh = 50;

        cancelTokenSource = new CancellationTokenSource();

        return Task.Run(() =>
        {
            while (true)
            {
                if (cancelTokenSource.Token.IsCancellationRequested)
                {
                    return;
                }
                Refresh();
            }
        }, cancelTokenSource.Token);
    }

    public void Tick()
    {
        Monitoring.Tick();
    }

    public IApplication? BuildApplication<TConfiguration>(
        int index, string name, string protocolType, string specification, INetwork network,
        string? group = null, int? groupAmount = null, TConfiguration? applicationConfig = default)
    {
        if (_nodes.ContainsKey((name, NodeType.Application)))
        {
            return null;
        }

        var serverAddressFull = GetAddress(name, protocolType, network.Address);

        var server = _networkBuilder
            .ApplicationFactory.Create(
                new NodeBuildOptions<object>(this, network, index, name, serverAddressFull,
                    group, groupAmount, protocolType, specification, null, applicationConfig)
                {
                    OptionsPath = OptionsPath
                });

        if (server == null)
        {
            return null;
        }

        _nodes.TryAdd((name, NodeType.Application), server);

        return server;
    }

    public void StopPeriodicRefresh()
    {
        cancelTokenSource?.Cancel();
    }
}