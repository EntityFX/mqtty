using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Counter;
using static System.Net.Mime.MediaTypeNames;

namespace EntityFX.MqttY.Network;

public class NetworkSimulator : INetworkSimulator
{
    private readonly ConcurrentDictionary<(string Address, NodeType NodeType), ILeafNode> _nodes = new();
    private readonly ConcurrentDictionary<string, INetwork> _networks = new();

    private long _tick = 0;
    private long _step = 0;
    private long _errors = 0;
    private long _packetId = 0;
    private int _countNodes = 0;

    private CancellationTokenSource? _cancelTokenSource;

    internal Exception? SimulationException;

    public event EventHandler<Exception>? OnError;

    public event EventHandler<long>? OnRefresh;

    private NetworkSimulatorCounters _counters;

    private readonly Stopwatch _stopwatch = new Stopwatch();
    private readonly Stopwatch _refreshStopwatch = new Stopwatch();
    private readonly TicksOptions _ticksOptions;

    public CounterGroup Counters
    {
        get => _counters;
        set
        {
            _counters = (NetworkSimulatorCounters)value;
        }
    }

    private Timer? _timer;

    public NetworkSimulator(
        IPathFinder pathFinder,
        INetworkLogger monitoring, TicksOptions ticksOptions, bool enableCounters)
    {
        PathFinder = pathFinder;
        Monitoring = monitoring;
        this._ticksOptions = ticksOptions;
        this.EnableCounters = enableCounters;
        PathFinder.NetworkGraph = this;

        _counters = new NetworkSimulatorCounters("NetworkSimulator", "NN", ticksOptions, enableCounters);
    }

    public string? OptionsPath { get; set; }

    public IPathFinder PathFinder { get; }

    public INetworkLogger Monitoring { get; }

    public IImmutableDictionary<string, INetwork> Networks => _networks.ToImmutableDictionary();

    public IImmutableDictionary<string, IClient> Clients => _nodes.Where(n => n.Key.NodeType == NodeType.Client)
        .ToDictionary(k => k.Key.Address, v => (IClient)v.Value).ToImmutableDictionary();
    public IImmutableDictionary<string, IServer> Servers => _nodes.Where(n => n.Key.NodeType == NodeType.Server)
        .ToDictionary(k => k.Key.Address, v => (IServer)v.Value).ToImmutableDictionary();

    public IImmutableDictionary<string, IApplication> Applications => _nodes.Where(n => n.Key.NodeType == NodeType.Application)
        .ToDictionary(k => k.Key.Address, v => (IApplication)v.Value).ToImmutableDictionary();

    public long TotalTicks => _tick;

    public bool Construction { get; set; }

    public long TotalSteps => _step;

    public bool WaitMode { get; private set; }
    public bool EnableCounters { get; private set; }

    public int CountNodes => _countNodes;

    public TimeSpan VirtualTime => _ticksOptions.TickPeriod * TotalTicks;

    public TimeSpan RealTime => _stopwatch.Elapsed;

    public long Errors => _errors;

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

    public INetworkPacket GetReversePacket(INetworkPacket packet, byte[] payload, string? category)
    {
        return new NetworkPacket<int>(
            Id: GetPacketId(),
            RequestId: packet.Id,
            ScopeId: packet.ScopeId,
            To: packet.From,
            ToIndex: packet.ToIndex,
            From: packet.To,
            FromIndex: packet.FromIndex,
            Payload: payload,
            FromType: packet.ToType,
            ToType: packet.FromType,
            Protocol: packet.Protocol,
            OutgoingTicks: packet.OutgoingTicks,
            HeaderBytes: packet.HeaderBytes,
            Category: category ?? packet.Category
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

        if (client.IsConnected)
        {
            client.Disconnect();
        }

        _nodes.Remove((clientAddress, NodeType.Client), out _);
        Interlocked.Decrement(ref _countNodes);
        client.Clear();
    }

    public void RemoveNetwork(string networkAddress)
    {
        var network = _networks.GetValueOrDefault(networkAddress);
        if (network == null)
        {
            return;
        }

        network.UnlinkAll();

        _networks.Remove(networkAddress, out _);

        UpdateRoutes();
        Interlocked.Decrement(ref _countNodes);
        network.Clear();
    }

    public void RemoveServer(string serverAddress)
    {
        if (!_nodes.ContainsKey((serverAddress, NodeType.Server)))
        {
            return;
        }

        var server = GetNode(serverAddress, NodeType.Server) as IServer;
        if (server == null)
        {
            return;
        }

        server.Stop();

        _nodes.Remove((serverAddress, NodeType.Server), out _);
        Interlocked.Decrement(ref _countNodes);
        server.Clear();
    }

    private void RemoveApplication(string name)
    {
        if (!_nodes.ContainsKey((name, NodeType.Application)))
        {
            return;
        }

        var application = GetNode(name, NodeType.Application) as IApplication;
        if (application == null)
        {
            return;
        }

        application.Stop();

        _nodes.Remove((name, NodeType.Application), out _);
        Interlocked.Decrement(ref _countNodes);
        application.Clear();
    }

    public bool Refresh(bool parallel)
    {
        _stopwatch.Start();
        try
        {
            var scope = Monitoring.BeginScope(TotalTicks, "Refresh sourceNetwork graph");
            Monitoring.Push(0, TotalTicks, NetworkLoggerType.Refresh, $"Refresh whole sourceNetwork", "Network", "Refresh", scope);

            if (parallel)
            {
                ParallelRefreshImplementation(scope);
            }
            else
            {
                RefreshImplementation(scope);
            }

            _counters.SetRealTime(_stopwatch.Elapsed);
            _counters.SetRealTime(VirtualTime);

            Monitoring.EndScope(TotalTicks, scope);

            return true;
        }
        catch (Exception ex)
        {
            _errors++;
            SimulationException = ex;
            return false;
        }
    }

    private void ParallelRefreshImplementation(NetworkLoggerScope? scope)
    {
        var bytes = Array.Empty<byte>();

        _counters.Refresh(TotalTicks, _step);

        Parallel.ForEach(_networks, network =>
        {
            Monitoring.Push(0, TotalTicks,
                network.Value, network.Value, bytes, NetworkLoggerType.Refresh, $"Refresh sourceNetwork {network.Key}",
                "Network", "Refresh", scope);
            network.Value.Refresh();
        });

        Parallel.ForEach(_nodes, node =>
        {
            Monitoring.Push(0, TotalTicks,
                node.Value, node.Value, bytes, NetworkLoggerType.Refresh, $"Refresh node {node.Key}", "Network", "Refresh",
                scope);
            node.Value.Refresh();
        });

        Tick();
    }

    private void RefreshImplementation(NetworkLoggerScope? scope)
    {
        var bytes = Array.Empty<byte>();

        _counters.Refresh(TotalTicks, _step);

        foreach (var network in _networks)
        {
            Monitoring.Push(0, TotalTicks,
                network.Value, network.Value, bytes, NetworkLoggerType.Refresh, $"Refresh sourceNetwork {network.Key}",
                "Network", "Refresh", scope);
            network.Value.Refresh();
        }

        foreach (var node in _nodes)
        {
            Monitoring.Push(0, TotalTicks,
                node.Value, node.Value, bytes, NetworkLoggerType.Refresh, $"Refresh node {node.Key}", "Network", "Refresh",
                scope);
            node.Value.Refresh();
        }
        Tick();
    }

    public bool Reset()
    {
        try
        {

            var scope = Monitoring.BeginScope(TotalTicks, "Reset sourceNetwork graph");
            Monitoring.Push(0, TotalTicks, NetworkLoggerType.Reset, $"Reset whole sourceNetwork", "Network", "Reset", scope);

            var bytes = Array.Empty<byte>();

            foreach (var network in _networks)
            {
                Monitoring.Push(0, TotalTicks,
                    network.Value, network.Value, bytes, NetworkLoggerType.Reset, $"Reset sourceNetwork {network.Key}",
                    "Network", "Refresh", scope);
                network.Value.Reset();
            }

            foreach (var node in _nodes)
            {
                Monitoring.Push(0, TotalTicks,
                    node.Value, node.Value, bytes, NetworkLoggerType.Reset, $"Reset node {node.Key}", "Network", "Reset",
                    scope);
                node.Value.Reset();
            }

            Monitoring.EndScope(TotalTicks, scope);

            Tick();

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
        if (_cancelTokenSource != null && _cancelTokenSource.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        _cancelTokenSource = new CancellationTokenSource();

        _timer = new Timer(Refreshed, this, 0, 1000);
        WaitMode = true;
        return Task.Run(() =>
        {
            bool refreshResult = true;

            while (true)
            {
                if (_cancelTokenSource.Token.IsCancellationRequested)
                {
                    WaitMode = false;
                    return;
                }
                _refreshStopwatch.Restart();
                refreshResult = Refresh(false);
                _refreshStopwatch.Reset();


                if (!refreshResult)
                {
                    Reset();

                    _cancelTokenSource.Cancel();

                    OnError?.Invoke(this, SimulationException!);
                    WaitMode = false;
                    break;
                }
            }
        }, _cancelTokenSource.Token);
    }

    private void Refreshed(object? state)
    {
        OnRefresh?.Invoke(this, TotalTicks);
    }

    public void Tick()
    {
        Interlocked.Increment(ref _tick);
    }

    public void Step()
    {
        Interlocked.Increment(ref _step);
    }


    public void StopPeriodicRefresh()
    {
        Reset();
        _timer?.Change(0, 0);
        _timer?.Dispose();
        _cancelTokenSource?.Cancel();
    }

    public bool AddClient(IClient client)
    {
        if (_nodes.ContainsKey((client.Name, NodeType.Client)))
        {
            return false;
        }

        var result = _nodes.TryAdd((client.Name, NodeType.Client), client);

        if (result)
        {
            ((NodeBase)client).NetworkSimulator = this;
        }

        Interlocked.Increment(ref _countNodes);

        return result;
    }

    public bool AddServer(IServer server)
    {
        if (_nodes.ContainsKey((server.Name, NodeType.Server)))
        {
            return false;
        }

        var result = _nodes.TryAdd((server.Name, NodeType.Server), server);

        if (result)
        {
            ((NodeBase)server).NetworkSimulator = this;
        }
        Interlocked.Increment(ref _countNodes);

        return result;
    }

    public bool AddApplication(IApplication application)
    {
        if (_nodes.ContainsKey((application.Name, NodeType.Application)))
        {
            return false;
        }

        var result = _nodes.TryAdd((application.Name, NodeType.Application), application);

        if (result)
        {
            ((ApplicationBase)application).NetworkSimulator = this;
        }

        Interlocked.Increment(ref _countNodes);

        return result;
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

        ((NodeBase)network).NetworkSimulator = this;

        Interlocked.Increment(ref _countNodes);

        return result;
    }

    public void UpdateRoutes()
    {
        if (Construction)
        {
            return;
        }
        PathFinder.Build();
        _counters.WithNetworks(_networks.Values);
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

    public void AddCounterValue<TValue>(string name, string shortName, TValue value) where TValue : struct, IEquatable<TValue>
    {
        _counters.AddCounterValue(name, shortName, value);
    }

    public bool RefreshWithCounters(bool parallel)
    {
        _refreshStopwatch.Restart();
        var refreshResult = Refresh(parallel);
        _refreshStopwatch.Reset();


        if (!refreshResult)
        {
            Reset();
            OnError?.Invoke(this, SimulationException!);
            return false;
        }

        return true;
    }

    public void Clear()
    {
        try
        {
            var scope = Monitoring.BeginScope(TotalTicks, "Clear sourceNetwork graph");
            Monitoring.Push(0, TotalTicks, NetworkLoggerType.Disconnect, $"Clear whole sourceNetwork", "Network", "Clean", scope);

            var bytes = Array.Empty<byte>();

            foreach (var node in _nodes.Values)
            {
                Monitoring.Push(0, TotalTicks,
                    node, node, Array.Empty<byte>(), NetworkLoggerType.Disconnect, $"Clean {node.NodeType} {node.Name}", node.NodeType.ToString(), "Clean",
                    scope);
                switch (node.NodeType)
                {
                    case NodeType.Network:
                        RemoveNetwork(node.Name);
                        break;
                    case NodeType.Server:
                        RemoveServer(node.Name);
                        break;
                    case NodeType.Client:
                        RemoveClient(node.Name);
                        break;
                    case NodeType.Application:
                        RemoveApplication(node.Name);
                        break;
                    case NodeType.Other:
                        break;
                }
            }

            foreach (var network in _networks)
            {
                Monitoring.Push(0, TotalTicks,
                    network.Value, network.Value, bytes, NetworkLoggerType.Disconnect, $"Clean sourceNetwork {network.Key}",
                    "Network", "Clean", scope);
                network.Value.Reset();
                RemoveNetwork(network.Key);
            }

            _counters.Clear();
            _stopwatch.Stop();
            _refreshStopwatch.Stop();

            Monitoring.EndScope(TotalTicks, scope);
        }
        catch (Exception ex)
        {
            SimulationException = ex;
        }
    }

    public long GetPacketId()
    {
        Interlocked.Increment(ref _packetId);
        return _packetId;
    }
}