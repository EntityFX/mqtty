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
    public bool IsQuiescent => _topology.Networks.Values.All(n => n.QueueSize == 0 &&
            (n is not IQuiescenceParticipant participant || participant.IsQuiescent)) &&
        _topology.Nodes.Values.OfType<IQuiescenceParticipant>().All(n => n.IsQuiescent);
    private readonly TopologyRegistry _topology = new();

    private long _errors = 0;
    private long _packetId = 0;
    private long _orderedTopologyVersion = -1;
    private KeyValuePair<string, INetwork>[] _orderedNetworks = Array.Empty<KeyValuePair<string, INetwork>>();
    // Parallel phase 1 starts with the densest networks. The shared client LAN
    // owns most nodes and takes far longer than a backend hop; starting it last
    // makes its worker the tick tail and inflates the phase fence.
    private KeyValuePair<string, INetwork>[] _parallelNetworks = Array.Empty<KeyValuePair<string, INetwork>>();
    private KeyValuePair<(string Address, NodeType NodeType), ILeafNode>[] _orderedNodes = Array.Empty<KeyValuePair<(string Address, NodeType NodeType), ILeafNode>>();
    private readonly object _measurementResetLock = new();
    private readonly SimulationClock _clock = new();
    private readonly TickPipeline _pipeline;

    // Persistent refresh workers (created lazily on the first parallel tick).
    private TickWorkerPool? _tickWorkerPool;
    private int _tickWorkerPoolSize;

    private readonly RuntimeOptions _runtime;
    private bool RefreshStatsEnabled => _runtime.RefreshStats;
    private long _refreshStatsTicks;
    private long _refreshStatsWall;
    private long _refreshStatsNetworkPhaseWall;
    private long _refreshStatsNodePhaseWall;
    private long _totalRefreshWallTicks;
    private long _refreshCallCount;
    private long[]? _nodeBusyTicks;
    private long[]? _nodeCalls;

    /// <summary>Steps snapshot taken at the start of the current refresh tick.</summary>
    public long CurrentRefreshSteps => _clock.CurrentRefreshSteps;

    private bool NodeStatsEnabled => _runtime.NodeStats;
    private bool ActiveOnly => _runtime.ActiveOnly;

    /// <summary>Accumulated wall time inside <see cref="Refresh"/> (diagnostics only).</summary>
    public double RefreshWallSeconds => (double)_totalRefreshWallTicks / Stopwatch.Frequency;

    /// <summary>Number of <see cref="Refresh"/> calls (diagnostics only).</summary>
    public long RefreshCalls => _refreshCallCount;

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
        _runtime = RuntimeOptions.FromEnvironment();
        PathFinder = pathFinder;
        Monitoring = monitoring;
        this._ticksOptions = ticksOptions;
        this.EnableCounters = enableCounters && !_runtime.DisableCounters;
        this.EnableMonitoring = false;
        PathFinder.NetworkGraph = this;

        _counters = new NetworkSimulatorCounters("NetworkSimulator", "NN", ticksOptions, enableCounters);
        _pipeline = new TickPipeline(PublishClock, RefreshTopology, CompleteTick);
    }

    public string? OptionsPath { get; set; }

    public IPathFinder PathFinder { get; }

    public INetworkLogger Monitoring { get; }

    public IDictionary<string, INetwork> Networks => _topology.Networks;

    public IDictionary<string, IClient> Clients => _topology.Clients;
    public IDictionary<string, IServer> Servers => _topology.Servers;

    public IDictionary<string, IApplication> Applications => _topology.Applications;

    public long TotalTicks => _clock.TotalTicks;

    public ISimulationClock Clock => _clock;

    public bool Construction { get; set; }

    public long TotalSteps => _clock.TotalSteps;

    public bool WaitMode { get; private set; }
    public bool EnableCounters { get; private set; }
    public bool EnableMonitoring { get; set; }

    public int CountNodes => _topology.CountNodes;

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

    public ILeafNode? GetNode(string address, NodeType nodeType) => _topology.GetNode(address, nodeType);

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
        if (!_topology.ContainsNode(clientAddress, NodeType.Client))
        {
            return;
        }

        var client = _topology.GetNode(clientAddress, NodeType.Client) as IClient;
        if (client == null)
        {
            return;
        }

        if (client.IsConnected)
        {
            client.Disconnect();
        }

        _topology.Nodes.TryRemove((clientAddress, NodeType.Client), out _);
        _topology.Clients.Remove(clientAddress);
        _topology.RemoveNodeCount();
        _topology.MarkChanged();
        client.Clear();
    }

    public void RemoveNetwork(string networkAddress)
    {
        var network = _topology.Networks.ContainsKey(networkAddress)
            ? _topology.Networks[networkAddress]
            : null;
        if (network == null)
        {
            return;
        }

        network.UnlinkAll();

        _topology.Networks.Remove(networkAddress);

        UpdateRoutes();
        _topology.RemoveNodeCount();
        _topology.MarkChanged();
        network.Clear();
    }

    public void RemoveServer(string serverAddress)
    {
        if (!_topology.ContainsNode(serverAddress, NodeType.Server))
        {
            return;
        }

        var server = _topology.GetNode(serverAddress, NodeType.Server) as IServer;
        if (server == null)
        {
            return;
        }

        server.Stop();

        _topology.Nodes.TryRemove((serverAddress, NodeType.Server), out _);
        _topology.Servers.Remove(serverAddress);
        _topology.RemoveNodeCount();
        _topology.MarkChanged();
        server.Clear();
    }

    private void RemoveApplication(string name)
    {
        if (!_topology.ContainsNode(name, NodeType.Application))
        {
            return;
        }

        var application = _topology.GetNode(name, NodeType.Application) as IApplication;
        if (application == null)
        {
            return;
        }

        application.Stop();

        _topology.Nodes.TryRemove((name, NodeType.Application), out _);
        _topology.Applications.Remove(name);
        _topology.RemoveNodeCount();
        _topology.MarkChanged();
        application.Clear();
    }

    public bool Refresh(bool parallel, int strategy)
    {
        var refreshStart = Stopwatch.GetTimestamp();
        _clock.BeginRefresh();
        _stopwatch.Start();
        try
        {
            NetworkLoggerScope? scope = null;
            if (EnableMonitoring)
            {
                scope = Monitoring.BeginScope(TotalTicks, "Refresh sourceNetwork graph");
                Monitoring.Push(0, TotalTicks, NetworkLoggerType.Refresh, $"Refresh whole sourceNetwork", "Network", "Refresh", scope);
            }

            var context = new TickContext(TotalTicks, CurrentRefreshSteps, parallel, strategy, scope);
            _pipeline.Run(in context);

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
        finally
        {
            _totalRefreshWallTicks += Stopwatch.GetTimestamp() - refreshStart;
            _refreshCallCount++;
        }
    }

    /// <summary>
    /// Below this combined networks+nodes count the parallel scheduler overhead
    /// exceeds the per-tick work, so the refresh falls back to plain loops.
    /// </summary>
    public int ParallelThreshold { get; set; } = 4;

    /// <summary>Upper bound of worker threads used by the parallel refresh (0 = default).</summary>
    public int MaxDegreeOfParallelism { get; set; } = 0;

    private static bool IsNodeIdle(object node) =>
        node is IQuiescenceParticipant participant && participant.IsQuiescent &&
        (node is not Node tracked || !tracked.HasPendingResponse);

    private void EnsureTopologySnapshot()
    {
        var snapshot = _topology.GetSnapshot();
        if (_orderedTopologyVersion != snapshot.Version)
        {
            _orderedNetworks = snapshot.Networks;
            _parallelNetworks = snapshot.Networks
                .OrderByDescending(network => network.Value.Nodes.Count)
                .ThenBy(network => network.Key, StringComparer.Ordinal)
                .ToArray();
            _orderedNodes = snapshot.Nodes;
            _orderedTopologyVersion = snapshot.Version;
            if (NodeStatsEnabled)
            {
                _nodeBusyTicks = null;
                _nodeCalls = null;
            }
        }
    }

    private void EnsureNodeStats(int count)
    {
        if (_nodeBusyTicks == null || _nodeBusyTicks.Length < count)
        {
            _nodeBusyTicks = new long[count];
            _nodeCalls = new long[count];
        }
    }

    /// <summary>
    /// Diagnostic dump (MQTTY_NODE_STATS=1): per-node-type and top-node busy time.
    /// It measures the node body only, so the caller can compare the parallel
    /// work sum with the sequential refresh wall.
    /// </summary>
    private void DumpNodeStats()
    {
        if (!NodeStatsEnabled || _nodeBusyTicks == null || _nodeCalls == null)
        {
            return;
        }

        var count = Math.Min(_orderedNodes.Length, _nodeBusyTicks.Length);
        var rows = Enumerable.Range(0, count)
            .Select(index => (Name: _orderedNodes[index].Key.Address, Type: _orderedNodes[index].Key.NodeType,
                Ticks: _nodeBusyTicks[index], Calls: _nodeCalls[index]))
            .Where(row => row.Calls > 0)
            .ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var frequency = (double)Stopwatch.Frequency;
        Console.Error.WriteLine($"node-stats total_ms={rows.Sum(r => r.Ticks) * 1000 / frequency:F1} calls={rows.Sum(r => r.Calls)}");
        foreach (var group in rows.GroupBy(row => row.Type).OrderByDescending(group => group.Sum(row => row.Ticks)))
        {
            Console.Error.WriteLine($"  {group.Key,-12} ms={group.Sum(r => r.Ticks) * 1000 / frequency:F1} calls={group.Sum(r => r.Calls)}");
        }
        foreach (var row in rows.OrderByDescending(r => r.Ticks).Take(8))
        {
            Console.Error.WriteLine($"  {row.Name,-28} {row.Type,-12} ms={row.Ticks * 1000 / frequency:F1} calls={row.Calls}");
        }
    }

    private void PublishClock(in TickContext context)
    {
        EnsureTopologySnapshot();

        if (NodeStatsEnabled)
        {
            EnsureNodeStats(_orderedNodes.Length);
        }

        // Publish the shared tick clock once per tick so counters do not need a
        // per-instance stamp walk (CounterClock).
        CounterClock.Publish(context.Tick, context.Steps);

        _counters.Refresh(context.Tick, context.Steps);
    }

    private void CompleteTick(in TickContext context)
    {
        // The clock advances before the wall/virtual counter sample, matching the
        // historical order of Refresh (tick first, then counter timestamps).
        Tick();
        _counters.SetRealTime(_stopwatch.Elapsed);
        _counters.SetVirtualTime(VirtualTime);
    }

    private void RefreshTopology(in TickContext context)
    {
        var networks = _orderedNetworks;
        var nodes = _orderedNodes;
        var tick = context.Tick;
        var scope = context.Scope;

        // The scheduler setup/teardown costs more than the refresh itself for
        // small topologies, so tiny simulations always run sequentially.
        if (!context.Parallel || networks.Length + nodes.Length < ParallelThreshold)
        {
            RefreshTopologySerial(context.Strategy, networks, nodes, tick, scope);
            return;
        }

        // Bulk-synchronous two-phase refresh on persistent worker threads:
        // every network is refreshed, then every node. The flat node list is
        // scheduled dynamically, so networks that own most of the nodes (the
        // shared client LAN in particular) no longer pin the tick to one
        // worker. Strategy 1 used to shard "network + its nodes", which made
        // that dense shard the serial bottleneck; the balanced two-phase
        // schedule is both faster and deterministic. Parallel refresh therefore
        // intentionally ignores `context.Strategy`.
        var pool = EnsureTickWorkerPool();
        var parallelNetworks = _parallelNetworks;
        var statsStart = RefreshStatsEnabled ? Stopwatch.GetTimestamp() : 0;
        // Networks are few and fairly uniform, so batching their claims is
        // fine. Nodes differ by orders of magnitude in cost (idle client vs
        // loaded broker); claiming them one at a time avoids a heavy tail.
        var networkChunk = _runtime.NetworkChunk
            ?? Math.Max(1, parallelNetworks.Length / (pool.WorkerCount * 4));
        var nodeChunk = _runtime.NodeChunk ?? 1;
        pool.Run(
            i => RefreshNetwork(parallelNetworks[i], tick, scope), parallelNetworks.Length,
            i => RefreshNode(nodes[i], tick, scope, i), nodes.Length,
            networkChunk, nodeChunk);
        if (RefreshStatsEnabled)
        {
            _refreshStatsTicks++;
            _refreshStatsWall += Stopwatch.GetTimestamp() - statsStart;
            _refreshStatsNetworkPhaseWall += pool.LastNetworkPhaseTicks;
            _refreshStatsNodePhaseWall += pool.LastNodePhaseTicks;
        }
    }

    private void RefreshTopologySerial(int strategy,
        KeyValuePair<string, INetwork>[] networks,
        KeyValuePair<(string Address, NodeType NodeType), ILeafNode>[] nodes,
        long tick, NetworkLoggerScope? scope)
    {
        if (strategy == 0)
        {
            foreach (var network in networks)
            {
                RefreshNetwork(network, tick, scope);
            }

            for (var index = 0; index < nodes.Length; index++)
            {
                RefreshNode(nodes[index], tick, scope, index);
            }
            return;
        }

        foreach (var network in networks)
        {
            RefreshNetwork(network, tick, scope);

            var networkNodes = network.Value.Nodes;
            foreach (var node in networkNodes)
            {
                RefreshNode(node, tick, scope);
            }
        }
    }

    private void RefreshNetwork(KeyValuePair<string, INetwork> network, long tick, NetworkLoggerScope? scope)
    {
        if (ActiveOnly && network.Value.QueueSize == 0)
        {
            return;
        }

        if (EnableMonitoring)
        {
            Monitoring.Push(0, tick,
                network.Value, network.Value, Array.Empty<byte>(), NetworkLoggerType.Refresh, $"Refresh sourceNetwork {network.Key}",
                "Network", "Refresh", scope);
        }
        network.Value.Refresh();
    }

    private void RefreshNode(KeyValuePair<string, INode> node, long tick, NetworkLoggerScope? scope)
    {
        if (ActiveOnly && IsNodeIdle(node.Value))
        {
            return;
        }

        if (EnableMonitoring)
        {
            Monitoring.Push(0, tick,
                node.Value, node.Value, Array.Empty<byte>(), NetworkLoggerType.Refresh, $"Refresh node {node.Key}", "Network", "Refresh",
                scope);
        }

        node.Value.Refresh();
    }

    private void RefreshNode(KeyValuePair<(string Address, NodeType NodeType), ILeafNode> node, long tick,
        NetworkLoggerScope? scope, int statsIndex)
    {
        if (ActiveOnly && IsNodeIdle(node.Value))
        {
            return;
        }

        var nodeStart = NodeStatsEnabled && statsIndex >= 0 ? Stopwatch.GetTimestamp() : 0;
        if (EnableMonitoring)
        {
            Monitoring.Push(0, tick,
                node.Value, node.Value, Array.Empty<byte>(), NetworkLoggerType.Refresh, $"Refresh node {node.Key}", "Network", "Refresh",
                scope);
        }

        node.Value.Refresh();

        if (NodeStatsEnabled && statsIndex >= 0)
        {
            _nodeBusyTicks![statsIndex] += Stopwatch.GetTimestamp() - nodeStart;
            _nodeCalls![statsIndex]++;
        }
    }

    /// <summary>
    /// Returns the persistent refresh pool, creating it on first use. The pool
    /// lives for the whole simulation so a tick no longer pays task creation,
    /// thread-pool wake-up and <see cref="Task.WaitAll"/> costs.
    /// </summary>
    private TickWorkerPool EnsureTickWorkerPool()
    {
        var desired = MaxDegreeOfParallelism > 0
            ? MaxDegreeOfParallelism
            : _runtime.ResolveWorkers();
        desired = Math.Clamp(desired, 1, 64);

        if (_tickWorkerPool != null && _tickWorkerPoolSize == desired)
        {
            return _tickWorkerPool;
        }

        _tickWorkerPool?.Dispose();
        _tickWorkerPool = new TickWorkerPool(desired, _runtime);
        _tickWorkerPoolSize = desired;
        return _tickWorkerPool;
    }

    /// <summary>
    /// Runs one parallel phase on the persistent tick workers and fences before
    /// returning. Engines use it for work that belongs between two simulator
    /// ticks (federation pumping) without paying a second fence.
    /// </summary>
    public void RunParallel(Action<int> body, int count, int chunkSize = 1)
    {
        if (count <= 0)
        {
            return;
        }

        var pool = EnsureTickWorkerPool();
        if (pool.WorkerCount <= 1)
        {
            for (var i = 0; i < count; i++) body(i);
            return;
        }

        pool.RunSingle(body, count, chunkSize);
    }

    public bool Reset()
    {
        try
        {
            var scope = Monitoring.BeginScope(TotalTicks, "Reset sourceNetwork graph");
            Monitoring.Push(0, TotalTicks, NetworkLoggerType.Reset, $"Reset whole sourceNetwork", "Network", "Reset", scope);

            var bytes = Array.Empty<byte>();

            foreach (var network in _topology.Networks)
            {
                Monitoring.Push(0, TotalTicks,
                    network.Value, network.Value, bytes, NetworkLoggerType.Reset, $"Reset sourceNetwork {network.Key}",
                    "Network", "Refresh", scope);
                network.Value.Reset();
            }

            foreach (var node in _topology.Nodes)
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
        _clock.Tick();
    }

    public void Step() => _clock.Step();

    public void ResetMeasurement()
    {
        lock (_measurementResetLock)
        {
            var startTick = TotalTicks;
            var sources = _topology.Nodes.Values.Cast<object>()
                .Concat(_topology.Networks.Values)
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
        if (_topology.ContainsNode(client.Name, NodeType.Client))
        {
            return false;
        }

        var cResult = _topology.ClientMap.TryAdd(client.Name, client);

        if (!cResult)
        {
            return false;
        }

        var result = _topology.Nodes.TryAdd((client.Name, NodeType.Client), client);

        if (result)
        {
            ((NodeBase)client).NetworkSimulator = this;
            _topology.AddNodeCount();
            _topology.MarkChanged();
        }
        else
        {
            _topology.ClientMap.TryRemove(client.Name, out _);
        }


        return result;
    }

    public bool AddServer(IServer server)
    {
        if (_topology.ContainsNode(server.Name, NodeType.Server))
        {
            return false;
        }

        var sResult = _topology.ServerMap.TryAdd(server.Name, server);

        if (!sResult)
        {
            return false;
        }

        var result = _topology.Nodes.TryAdd((server.Name, NodeType.Server), server);

        if (result)
        {
            ((NodeBase)server).NetworkSimulator = this;
            _topology.AddNodeCount();
            _topology.MarkChanged();
        }
        else
        {
            _topology.ServerMap.TryRemove(server.Name, out _);
        }

        return result;
    }

    public bool AddApplication(IApplication application)
    {
        if (_topology.ContainsNode(application.Name, NodeType.Application))
        {
            return false;
        }

        var aResult = _topology.ApplicationMap.TryAdd(application.Name, application);

        if (!aResult)
        {
            return false;
        }

        var result = _topology.Nodes.TryAdd((application.Name, NodeType.Application), application);

        if (result)
        {
            ((ApplicationBase)application).NetworkSimulator = this;
            _topology.AddNodeCount();
            _topology.MarkChanged();
        }
        else
        {
            _topology.ApplicationMap.TryRemove(application.Name, out _);
        }


        return result;
    }

    public bool AddNetwork(INetwork network)
    {
        if (_topology.Networks.ContainsKey(network.Name))
        {
            return false;
        }

        var result = _topology.NetworkMap.TryAdd(network.Name, network);

        if (!result)
        {
            return false;
        }

        UpdateRoutes();

        ((NodeBase)network).NetworkSimulator = this;

        _topology.AddNodeCount();
        _topology.MarkChanged();

        return result;
    }

    public void UpdateRoutes()
    {
        if (Construction)
        {
            return;
        }
        PathFinder.Build();
        _counters.WithNetworks(_topology.Networks.Values);
    }

    public bool Link(string sourceNetwork, string destinationNetwork)
    {
        var source = _topology.NetworkMap.TryGetValue(sourceNetwork, out var sourceValue) ? sourceValue : null;
        if (source == null)
        {
            return false;
        }

        var destination = _topology.NetworkMap.TryGetValue(destinationNetwork, out var destinationValue) ? destinationValue : null;
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

            foreach (var node in _topology.Nodes.Values)
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

            foreach (var network in _topology.Networks)
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
        finally
        {
            if (RefreshStatsEnabled && _refreshStatsTicks > 0)
            {
                var frequency = (double)Stopwatch.Frequency;
                Console.Error.WriteLine(
                    $"refresh-stats ticks={_refreshStatsTicks} workers={_tickWorkerPoolSize} " +
                    $"wall_ms={_refreshStatsWall * 1000 / frequency:F1} " +
                    $"network_ms={_refreshStatsNetworkPhaseWall * 1000 / frequency:F1} " +
                    $"node_ms={_refreshStatsNodePhaseWall * 1000 / frequency:F1} " +
                    $"us_per_tick={_refreshStatsWall / frequency * 1e6 / _refreshStatsTicks:F1}");

                if (_tickWorkerPool != null && _runtime.WorkerStats)
                {
                    var p1 = _tickWorkerPool.Phase1Busy;
                    var p2 = _tickWorkerPool.Phase2Busy;
                    var p1Sum = p1.Sum();
                    var p1Max = p1.Length > 0 ? p1.Max() : 0;
                    var p2Sum = p2.Sum();
                    var p2Max = p2.Length > 0 ? p2.Max() : 0;
                    var b = _tickWorkerPool.BarrierBusy;
                    var bSum = b.Sum();
                    var bMax = b.Length > 0 ? b.Max() : 0;
                    var wall = _refreshStatsWall;
                    Console.Error.WriteLine(
                        $"worker-stats phase1 sum_ms={p1Sum * 1000 / frequency:F1} max_ms={p1Max * 1000 / frequency:F1} " +
                        $"phase2 sum_ms={p2Sum * 1000 / frequency:F1} max_ms={p2Max * 1000 / frequency:F1} " +
                        $"barrier sum_ms={bSum * 1000 / frequency:F1} max_ms={bMax * 1000 / frequency:F1} " +
                        $"barrier_each=[{string.Join(",", b.Select(v => (v * 1000 / frequency).ToString("F0")))}] " +
                        $"p2_each=[{string.Join(",", p2.Select(v => (v * 1000 / frequency).ToString("F0")))}] " +
                        $"wall_ms={wall * 1000 / frequency:F1} " +
                        $"p2_parallelism={(p2Max > 0 ? (double)p2Sum / p2Max : 0):F2}");
                }
            }

            DumpNodeStats();
            DisposeTickWorkerPool();
        }
    }

    private void DisposeTickWorkerPool()
    {
        _tickWorkerPool?.Dispose();
        _tickWorkerPool = null;
        _tickWorkerPoolSize = 0;
    }

    private bool NonAtomicPacketId => _runtime.NonAtomicPacketId;

    public long GetPacketId()
    {
        if (NonAtomicPacketId)
        {
            return ++_packetId;
        }

        return Interlocked.Increment(ref _packetId);
    }
}
