using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Xml.Linq;
using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Counter;
using static System.Net.Mime.MediaTypeNames;

namespace EntityFX.MqttY.Network;

public enum SimulationRunMode
{
    Turbo,
    RealTime,
    FastForward
}

public class NetworkSimulator : INetworkSimulator
{
    public bool IsQuiescent => _networks.Values.All(n => n.QueueSize == 0 &&
            (n is not IQuiescenceParticipant participant || participant.IsQuiescent)) &&
        _nodes.Values.OfType<IQuiescenceParticipant>().All(n => n.IsQuiescent);
    private readonly ConcurrentDictionary<(string Address, NodeType NodeType), ILeafNode> _nodes = new();
    private readonly ConcurrentDictionary<string, INetwork> _networks = new();
    private readonly ConcurrentDictionary<string, IClient> _clients = new();
    private readonly ConcurrentDictionary<string, IServer> _servers = new();
    private readonly ConcurrentDictionary<string, IApplication> _apps = new();

    private long _tick = 0;
    // Per-worker step deltas summed on read: removes the interlocked cache-line
    // contention of a single shared counter on every Send/Receive (R4).
    private readonly ThreadLocal<long> _stepDeltas = new(trackAllValues: true);
    private long _errors = 0;
    private long _packetId = 0;
    private int _countNodes = 0;
    private long _topologyVersion;
    private long _orderedTopologyVersion = -1;
    private KeyValuePair<string, INetwork>[] _orderedNetworks = Array.Empty<KeyValuePair<string, INetwork>>();
    private KeyValuePair<(string Address, NodeType NodeType), ILeafNode>[] _orderedNodes = Array.Empty<KeyValuePair<(string Address, NodeType NodeType), ILeafNode>>();
    private readonly object _measurementResetLock = new();

    public SimulationRunMode RunMode { get; set; } = SimulationRunMode.RealTime;
    public double SpeedMultiplier { get; set; } = 1.0;

    private CancellationTokenSource? _cancelTokenSource;

    internal Exception? _lastSimulationException;

    /// <summary>Last exception captured by the simulator (null when healthy); used for diagnostics of refresh failures.</summary>
    public Exception? SimulationException => _lastSimulationException;

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
        ticksOptions.Validate();
        PathFinder = pathFinder;
        Monitoring = monitoring;
        this._ticksOptions = ticksOptions;
        this.EnableCounters = enableCounters;
        this.EnableMonitoring = false;
        PathFinder.NetworkGraph = this;

        _counters = new NetworkSimulatorCounters("NetworkSimulator", "NN", ticksOptions, enableCounters);
    }

    public string? OptionsPath { get; set; }

    public IPathFinder PathFinder { get; }

    public INetworkLogger Monitoring { get; }

    public IDictionary<string, INetwork> Networks => _networks;

    public IDictionary<string, IClient> Clients => _clients;
    public IDictionary<string, IServer> Servers => _servers;

    public IDictionary<string, IApplication> Applications => _apps;

    public long TotalTicks => _tick;

    public bool Construction { get; set; }

    public long TotalSteps
    {
        get
        {
            long sum = 0;
            foreach (var delta in _stepDeltas.Values)
            {
                sum += delta;
            }
            return sum;
        }
    }

    public bool WaitMode { get; private set; }
    public bool EnableCounters { get; private set; }
    public bool EnableMonitoring { get; set; }

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
            ToIndex: packet.FromIndex,
            From: packet.To,
            FromIndex: packet.ToIndex,
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
        _clients.Remove(clientAddress, out _);
        Interlocked.Decrement(ref _countNodes);
        Interlocked.Increment(ref _topologyVersion);
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
        Interlocked.Increment(ref _topologyVersion);
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
        _servers.Remove(serverAddress, out _);
        Interlocked.Decrement(ref _countNodes);
        Interlocked.Increment(ref _topologyVersion);
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
        _apps.Remove(name, out _);
        Interlocked.Decrement(ref _countNodes);
        Interlocked.Increment(ref _topologyVersion);
        application.Clear();
    }

    public bool Refresh(bool parallel, int strategy)
    {
        _stopwatch.Start();
        try
        {
            NetworkLoggerScope? scope = null;
            if (EnableMonitoring)
            {
                scope = Monitoring.BeginScope(TotalTicks, "Refresh sourceNetwork graph");
                Monitoring.Push(0, TotalTicks, NetworkLoggerType.Refresh, $"Refresh whole sourceNetwork", "Network", "Refresh", scope);
            }

            if (parallel)
            {
                ParallelRefreshImplementation(scope, strategy);
            }
            else
            {
                RefreshImplementation(scope, strategy);
            }

            _counters.SetRealTime(_stopwatch.Elapsed);
            _counters.SetVirtualTime(VirtualTime);

            if (EnableMonitoring)
            {
                Monitoring.EndScope(TotalTicks, scope);
            }

            return true;
        }
        catch (Exception ex)
        {
            _errors++;
            _lastSimulationException = ex;
            return false;
        }
    }

    /// <summary>
    /// Below this combined networks+nodes count the parallel scheduler overhead
    /// exceeds the per-tick work, so the refresh falls back to plain loops.
    /// </summary>
    public int ParallelThreshold { get; set; } = 32;

    /// <summary>Upper bound of worker threads used by the parallel refresh (0 = default).</summary>
    public int MaxDegreeOfParallelism { get; set; } = 0;

    private void EnsureTopologySnapshot()
    {
        var version = Volatile.Read(ref _topologyVersion);
        if (_orderedTopologyVersion != version)
        {
            _orderedNetworks = _networks.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
            _orderedNodes = _nodes.OrderBy(item => item.Key.Address, StringComparer.Ordinal).ThenBy(item => item.Key.NodeType).ToArray();
            _orderedTopologyVersion = version;
        }
    }

    private void ParallelRefreshImplementation(NetworkLoggerScope? scope, int strategy)
    {
        var bytes = Array.Empty<byte>();

        EnsureTopologySnapshot();

        // Publish the shared tick clock once per tick so counters do not need a
        // per-instance stamp walk (CounterClock).
        CounterClock.Publish(TotalTicks, TotalSteps);

        _counters.Refresh(TotalTicks, TotalSteps);

        var networks = _orderedNetworks;
        var nodes = _orderedNodes;

        void RefreshNetwork(KeyValuePair<string, INetwork> network)
        {
            if (EnableMonitoring)
            {
                Monitoring.Push(0, TotalTicks,
                    network.Value, network.Value, bytes, NetworkLoggerType.Refresh, $"Refresh sourceNetwork {network.Key}",
                    "Network", "Refresh", scope);
            }
            network.Value.Refresh();
        }

        void RefreshNode(KeyValuePair<(string Address, NodeType NodeType), ILeafNode> node)
        {
            if (EnableMonitoring)
            {
                Monitoring.Push(0, TotalTicks,
                    node.Value, node.Value, bytes, NetworkLoggerType.Refresh, $"Refresh node {node.Key}", "Network", "Refresh",
                    scope);
            }
            node.Value.Refresh();
        }

        // The scheduler setup/teardown costs more than the refresh itself for
        // small topologies, so tiny simulations always run sequentially.
        var runParallel = networks.Length + nodes.Length >= ParallelThreshold;

        if (!runParallel)
        {
            foreach (var network in networks)
            {
                RefreshNetwork(network);
            }

            foreach (var node in nodes)
            {
                RefreshNode(node);
            }
        }
        else if (strategy == 0)
        {
            // Range-partitioned worker pool: contiguous stripes per worker give
            // better cache locality and avoid Parallel's TaskReplicator /
            // loop-state overhead, which is a top CPU consumer at this tick rate.
            RunRanges(networks.Length, i => RefreshNetwork(networks[i]));
            RunRanges(nodes.Length, i => RefreshNode(nodes[i]));
        }
        else
        {
            // Shard per network: the network and all of its nodes are refreshed
            // on the same worker, so they never contend for the network queue.
            RunRanges(networks.Length, i =>
            {
                RefreshNetwork(networks[i]);
                var shardNodes = networks[i].Value.Nodes;
                foreach (var node in shardNodes)
                {
                    RefreshNode(new KeyValuePair<(string Address, NodeType NodeType), ILeafNode>(
                        (node.Key, node.Value.NodeType), (ILeafNode)node.Value));
                }
            });
        }

        Tick();
    }

    /// <summary>
    /// Executes <paramref name="body"/> over [0, itemCount) using a fixed pool of
    /// range-partitioned tasks (contiguous stripes) with a final barrier.
    /// </summary>
    private void RunRanges(int itemCount, Action<int> body)
    {
        if (itemCount == 0)
        {
            return;
        }

        var workerCount = MaxDegreeOfParallelism > 0
            ? Math.Min(MaxDegreeOfParallelism, itemCount)
            : Math.Min(Environment.ProcessorCount, itemCount);

        if (workerCount <= 1)
        {
            for (var i = 0; i < itemCount; i++)
            {
                body(i);
            }
            return;
        }

        var chunk = (itemCount + workerCount - 1) / workerCount;
        var tasks = new Task[workerCount];
        for (var w = 0; w < workerCount; w++)
        {
            var from = w * chunk;
            var to = Math.Min(itemCount, from + chunk);
            if (from >= to)
            {
                tasks[w] = Task.CompletedTask;
                continue;
            }
            tasks[w] = Task.Run(() =>
            {
                for (var i = from; i < to; i++)
                {
                    body(i);
                }
            });
        }
        Task.WaitAll(tasks);
    }

    private void RefreshImplementation(NetworkLoggerScope? scope, int strategy)
    {
        var bytes = Array.Empty<byte>();

        EnsureTopologySnapshot();

        CounterClock.Publish(TotalTicks, TotalSteps);

        _counters.Refresh(TotalTicks, TotalSteps);

        if (strategy == 0)
        {
            foreach (var network in _orderedNetworks)
            {
                if (EnableMonitoring)
                {
                    Monitoring.Push(0, TotalTicks,
                        network.Value, network.Value, bytes, NetworkLoggerType.Refresh, $"Refresh sourceNetwork {network.Key}",
                        "Network", "Refresh", scope);
                }
                network.Value.Refresh();
            }

            foreach (var node in _orderedNodes)
            {
                if (EnableMonitoring)
                {
                    Monitoring.Push(0, TotalTicks,
                        node.Value, node.Value, bytes, NetworkLoggerType.Refresh, $"Refresh node {node.Key}", "Network", "Refresh",
                        scope);
                }
                node.Value.Refresh();
            }
        }
        else
        {
            foreach (var network in _orderedNetworks)
            {
                if (EnableMonitoring)
                {
                    Monitoring.Push(0, TotalTicks,
                        network.Value, network.Value, bytes, NetworkLoggerType.Refresh, $"Refresh sourceNetwork {network.Key}",
                        "Network", "Refresh", scope);
                }
                network.Value.Refresh();

                var nodes = network.Value.Nodes;
                foreach (var node in nodes)
                {
                    if (EnableMonitoring)
                    {
                        Monitoring.Push(0, TotalTicks,
                            node.Value, node.Value, bytes, NetworkLoggerType.Refresh, $"Refresh node {node.Key}", "Network", "Refresh",
                            scope);
                    }
                    node.Value.Refresh();
                }
            }
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
            _lastSimulationException = ex;
            return false;
        }
    }

    public async Task StartPeriodicRefreshAsync()
    {
        if (_cancelTokenSource != null && _cancelTokenSource.IsCancellationRequested)
        {
            return;
        }

        _cancelTokenSource = new CancellationTokenSource();

        _timer = new Timer(Refreshed, this, 0, 1000);
        WaitMode = true;

        _ = Task.Run(async () =>
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
                refreshResult = Refresh(false, 0);
                _refreshStopwatch.Reset();

                if (!refreshResult)
                {
                    Reset();
                    _cancelTokenSource.Cancel();
                    OnError?.Invoke(this, SimulationException!);
                    WaitMode = false;
                    break;
                }

                switch (RunMode)
                {
                    case SimulationRunMode.Turbo:
                        await Task.Yield();
                        break;
                    case SimulationRunMode.RealTime:
                        await Task.Delay(_ticksOptions.TickPeriod, _cancelTokenSource.Token);
                        break;
                    case SimulationRunMode.FastForward:
                        var delayTicks = (long)(_ticksOptions.TickPeriod.Ticks / Math.Max(1.0, SpeedMultiplier));
                        await Task.Delay(TimeSpan.FromTicks(delayTicks), _cancelTokenSource.Token);
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

    public void Step() => _stepDeltas.Value++;

    public void ResetMeasurement()
    {
        lock (_measurementResetLock)
        {
            var startTick = TotalTicks;
            var sources = _nodes.Values.Cast<object>()
                .Concat(_networks.Values)
                .OfType<IMeasurementSource>()
                .Distinct()
                .ToArray();
            foreach (var source in sources)
            {
                source.ResetMeasurement(startTick);
            }
        }
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

        var cResult = _clients.TryAdd(client.Name, client);

        if (!cResult)
        {
            return false;
        }

        var result = _nodes.TryAdd((client.Name, NodeType.Client), client);

        if (result)
        {
            ((NodeBase)client).NetworkSimulator = this;
            Interlocked.Increment(ref _countNodes);
        Interlocked.Increment(ref _topologyVersion);
        }


        return result;
    }

    public bool AddServer(IServer server)
    {
        if (_nodes.ContainsKey((server.Name, NodeType.Server)))
        {
            return false;
        }

        var sResult = _servers.TryAdd(server.Name, server);

        if (!sResult)
        {
            return false;
        }

        var result = _nodes.TryAdd((server.Name, NodeType.Server), server);

        if (result)
        {
            ((NodeBase)server).NetworkSimulator = this;
            Interlocked.Increment(ref _countNodes);
        Interlocked.Increment(ref _topologyVersion);
        }

        return result;
    }

    public bool AddApplication(IApplication application)
    {
        if (_nodes.ContainsKey((application.Name, NodeType.Application)))
        {
            return false;
        }

        var aResult = _apps.TryAdd(application.Name, application);

        if (!aResult)
        {
            return false;
        }

        var result = _nodes.TryAdd((application.Name, NodeType.Application), application);

        if (result)
        {
            ((ApplicationBase)application).NetworkSimulator = this;
            Interlocked.Increment(ref _countNodes);
        Interlocked.Increment(ref _topologyVersion);
        }


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
        Interlocked.Increment(ref _topologyVersion);

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

    public bool RefreshWithCounters(bool parallel, int strategy)
    {
        _refreshStopwatch.Restart();
        var refreshResult = Refresh(parallel, strategy);
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
            _lastSimulationException = ex;
        }
    }

    public long GetPacketId()
    {
        Interlocked.Increment(ref _packetId);
        return _packetId;
    }
}
