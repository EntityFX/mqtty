using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Counter;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using NetworkLoggerType = EntityFX.MqttY.Contracts.NetworkLogger.NetworkLoggerType;

namespace EntityFX.MqttY.Network;

public class Network : NodeBase, INetwork, IQuiescenceParticipant
{
    public virtual bool IsQuiescent => _monitoringPacketsQueue.IsEmpty &&
        Nodes.Values.OfType<IQuiescenceParticipant>().All(n => n.IsQuiescent);
    private readonly Dictionary<string, INetwork> _linkedNetworks = new();
    private readonly Dictionary<string, IServer> _servers = new();
    private readonly Dictionary<string, IClient> _clients = new();
    private readonly Dictionary<string, IApplication> _applications = new();

    /// <summary>
    /// TODO: Add max size limit
    /// </summary>
    private ConcurrentQueue<NetworkPacketSlot> _monitoringPacketsQueue = new();

    // Number of items currently in _monitoringPacketsQueue. ConcurrentQueue.Count
    // walks the segment list and takes locks, which turned every send into a
    // serialised operation on the dense client LAN; this counter is O(1).
    private int _queueLength;

    // Deferred (not-yet-due) network packets, split from the ready queue so each
    // tick only touches packets once and never re-enqueues the whole backlog
    // (O(ready) per tick instead of O(whole queue)) (R1).
    private List<NetworkPacketSlot> _deferredPackets = new();
    private List<NetworkPacketSlot> _deferredPacketsNext = new();

    private readonly NetworkOptions _networkTypeOption;
    private readonly object _admissionLock = new();
    private readonly IAdmissionPolicy _admission;

    //private Dictionary<Guid, NetworkMonitoringPacket> _monitoringPacketsQueue = new();

    private readonly NetworkCounters _networkCounters;
    private CounterGroup _counters;

    private readonly object _countersLock = new object();
    private CancellationTokenSource? _cancelTokenSource;
    public string NetworkType { get; }

    private CounterGroup? _countersSnapshot;
    private int _countersSnapshotVersion = -1;

    public override CounterGroup Counters
    {
        get
        {
            // Snapshot is rebuilt only when the node set changes; this getter is
            // touched on every refresh tick, so allocations here dominate CPU.
            var version = _nodesVersion;
            if (_countersSnapshot == null || _countersSnapshotVersion != version)
            {
                _counters.Counters = new ICounter[]
                    {
                        _networkCounters,
                        new CounterGroup(Name, "SS", "Servers", "SG")
                        {
                            Counters = _servers.Values.ToArray().Select(s =>s.Counters).ToArray()
                        },
                        new CounterGroup(Name, "CS", "Clients", "CG")
                        {
                            Counters = _clients.Values.ToArray().Select(s =>s.Counters).ToArray()
                        },
                        new CounterGroup(Name, "AS", "Applications", "AG")
                        {
                            Counters = _applications.Values.ToArray().Select(s =>s.Counters).ToArray()
                        },
                    };
                _countersSnapshot = _counters;
                _countersSnapshotVersion = version;
            }
            return _countersSnapshot;
        }
        set
        {
            _counters = value;
            _countersSnapshot = null;
            _countersSnapshotVersion = -1;
        }
    }

    public IReadOnlyDictionary<string, INetwork> LinkedNearestNetworks => _linkedNetworks.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IServer> Servers => _servers.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IClient> Clients => _clients.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IApplication> Applications => _applications.ToImmutableDictionary();

    private IReadOnlyDictionary<string, INode>? _nodesCache;
    private int _nodesCacheVersion = -1;
    private int _nodesVersion = 0;

    public IReadOnlyDictionary<string, INode> Nodes
    {
        get
        {
            if (_nodesCache == null || _nodesCacheVersion != _nodesVersion)
            {
                _nodesCache = _clients.Values.OfType<INode>().ToArray()
                    .Concat(_servers.Values.OfType<INode>().ToArray())
                    .Concat(_applications.Values.OfType<INode>().ToArray())
                    .ToDictionary(n => n.Name, n => n).ToImmutableDictionary();
                _nodesCacheVersion = _nodesVersion;
            }
            return _nodesCache;
        }
    }


    public override NodeType NodeType => NodeType.Network;

    public long InboundPacketCount => _networkCounters.InboundPacketCount;
    public long OutboundPacketCount => _networkCounters.OutboundPacketCount;
    public long TransferCount => _networkCounters.TransferCount;

    public long QueueSize =>
        Volatile.Read(ref _queueLength) + _deferredPackets.Count + _deferredPacketsNext.Count;

    public Network(
        int index, string name, string address, string networkType,
        NetworkOptions networkTypeOption, TicksOptions ticksOptions, bool enableCounters)
        : base(index, name, address)
    {
        networkTypeOption.Validate();
        ticksOptions.Validate();
        this._networkTypeOption = networkTypeOption;
        _networkCounters = new NetworkCounters(Name, "NC", ticksOptions, enableCounters,
            throughputWindowTicks: networkTypeOption.ThroughputWindowTicks);
        _counters = new CounterGroup(Name, "NN", "Network", "NG", enableCounters);
        var throughputBudgetBytes = networkTypeOption.CapacityBytesPerSecond *
            ticksOptions.TickPeriod.TotalSeconds * networkTypeOption.ThroughputWindowTicks;
        _admission = new TokenWindowAdmission(throughputBudgetBytes, networkTypeOption.ThroughputWindowTicks);
        NetworkType = networkType;
    }

    public bool AddClient(IClient client)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));

        if (_clients.TryGetValue(client.Name, out var existingClient))
        {
            return ReferenceEquals(existingClient, client);
        }
        _clients[client.Name] = client;

        ((Client)client).Network = this;

        _nodesVersion++;

        return true;
    }

    public bool RemoveClient(string client)
    {
        var clientNode = _clients.GetValueOrDefault(client);
        if (clientNode == null)
        {
            return false;
        }

        if (clientNode.IsConnected)
        {
            clientNode.Disconnect();
        }

        var removed = _clients.Remove(clientNode.Name);
        if (removed) _nodesVersion++;
        return removed;
    }


    public bool Link(INetwork network)
    {
        if (network == null) throw new ArgumentNullException(nameof(network));

        if (_linkedNetworks.ContainsKey(network.Name))
        {
            return false;
        }
        _linkedNetworks[network.Name] = network;

        var result = network.Link(this);

        if (NetworkSimulator!.EnableMonitoring)
        {
            NetworkSimulator.Monitoring.Push(0, NetworkSimulator.TotalTicks, this, network, null, NetworkLoggerType.Link,
                $"Link network {this.Name} <-> {network.Name}", "Network", "Link", Scope);
        }

        return true;
    }

    public bool Unlink(INetwork network)
    {
        if (network == null) throw new ArgumentNullException(nameof(network));

        if (!_linkedNetworks.ContainsKey(network.Name))
        {
            return false;
        }

        var result = _linkedNetworks.Remove(network.Name);
        if (!result)
        {
            _linkedNetworks[network.Name] = network;
        }

        if (NetworkSimulator!.EnableMonitoring)
        {
            NetworkSimulator.Monitoring.Push(0, NetworkSimulator.TotalTicks, this, network, null, NetworkLoggerType.Unlink,
                $"Unlink network {this.Name} from {network.Name}", "Network", "Unlink");
        }

        return result;
    }

    public bool UnlinkAll()
    {
        if (_counters != null)
        {
            _counters.Clear();
        }

        _applications.Clear();
        _clients.Clear();
        _servers.Clear();

        foreach (var network in _linkedNetworks.Values)
        {
            var result = network.Unlink(this);
            if (!result)
            {
                return false;
            }
        }

        return true;
    }

    public bool AddServer(IServer server)
    {
        if (server == null) throw new ArgumentNullException(nameof(server));

        if (_servers.ContainsKey(server.Name))
        {
            return false;
        }
        _servers[server.Name] = server;

        ((Server)server).Network = this;

        _nodesVersion++;

        return true;
    }

    public bool RemoveServer(string id)
    {
        if (!_servers.ContainsKey(id))
        {
            return false;
        }

        _servers.Remove(id);

        _nodesVersion++;

        return true;
    }

    internal bool TransferNext(ref NetworkPacketSlot networkPacket)
    {
        if (networkPacket.NextHop >= networkPacket.Path.Length)
        {
            networkPacket.Type = NetworkPacketType.Local;
        }

        return TryEnqueue(networkPacket, isTransfer: true);
    }

    //TODO: If queue limit is exceeded then reject Send
    //bool?
    //timeout?
    protected override bool SendImplementation(INetworkPacket packet)
    {
        var networkPacket = GetNetworkPacketType(packet);
        return TryEnqueue(networkPacket);
    }

    private bool TryEnqueue(NetworkPacketSlot networkPacket, bool isTransfer = false)
    {
        var packet = networkPacket.Packet;

        // Lock-free fast-fail for the common overload case; avoids touching the
        // shared admission window when the queue is already saturated.
        if (Volatile.Read(ref _queueLength) >= _networkTypeOption.QueueCapacity)
        {
            _networkCounters.Refuse();
            return false;
        }

        var tick = NetworkSimulator!.TotalTicks;
        var bytes = packet.PacketBytes;
        if (!_admission.TryReserve(tick, bytes))
        {
            _networkCounters.Refuse();
            return false;
        }

        // Re-check after the byte claim: another worker may have filled the
        // queue between the fast-fail and the reservation. A failed second
        // check releases the claim without enqueueing a window entry.
        if (Volatile.Read(ref _queueLength) >= _networkTypeOption.QueueCapacity)
        {
            _admission.Rollback(bytes);
            _networkCounters.Refuse();
            return false;
        }

        if (isTransfer) packet.DecrementTtl();
        _monitoringPacketsQueue.Enqueue(networkPacket);
        Interlocked.Increment(ref _queueLength);
        _admission.Commit(tick, bytes);
        _networkCounters.CountInbound(packet);
        return true;
    }

    private NetworkPacketSlot GetNetworkPacketType(INetworkPacket packet)
    {
        var destionationNode = GetDestinationNode(packet.To!, packet.ToType);

        if (destionationNode != null)
        {
            return new NetworkPacketSlot(NetworkSimulator!.TotalTicks,
                _networkTypeOption.TransferTicks, passTillNextTick: false, packet,
                Array.Empty<INetwork>(), 0, NetworkPacketType.Local, destionationNode);
        }

        var sourceNode = NetworkSimulator!.GetNode(packet.From, packet.FromType);

        var fromNetwork = NetworkSimulator.GetNetworkByNode(packet.From, packet.FromType);

        var toNetwork = NetworkSimulator.GetNetworkByNode(packet.To, packet.ToType);

        if (sourceNode == null || fromNetwork == null || toNetwork == null)
        {
            return new NetworkPacketSlot(NetworkSimulator!.TotalTicks,
                _networkTypeOption.TransferTicks, passTillNextTick: false, packet,
                Array.Empty<INetwork>(), 0, NetworkPacketType.Unreachable, null);
        }

        var pathToRemote = NetworkSimulator.PathFinder.GetPath(fromNetwork, toNetwork).ToArray();
        destionationNode = (toNetwork as Network)?.GetDestinationNode(packet.To!, packet.ToType);

        return new NetworkPacketSlot(NetworkSimulator!.TotalTicks,
            _networkTypeOption.TransferTicks, passTillNextTick: true, packet,
            pathToRemote, 0, NetworkPacketType.Remote, destionationNode);
    }


    private bool SendToLocal(INetwork network, NetworkPacketSlot networkPacket)
    {
        var packet = networkPacket.Packet;
        if (string.IsNullOrEmpty(packet.From))
        {
            throw new ArgumentException($"'{nameof(packet.To)}' cannot be null or empty.", nameof(packet.To));
        }

        if (networkPacket.DestinationNode == null)
        {
            return false;
        }


        if (NetworkSimulator!.EnableMonitoring)
        {
            NetworkSimulator.Monitoring.Push(networkPacket.Packet.Id, NetworkSimulator.TotalTicks, network, networkPacket.DestinationNode, packet.Payload, NetworkLoggerType.Push,
                $"Push packet from network to node: {network.Name} ~> {networkPacket.DestinationNode.Name}",
                "Network", packet.Category, null, packet.Ttl, queueLength: _monitoringPacketsQueue.Count);
            NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);
        }

        _networkCounters.CountOutbound(packet);
        return networkPacket.DestinationNode!.Receive(packet);
    }


    private bool SendToRemote(NetworkPacketSlot networkPacket, out bool deferred)
    {
        deferred = false;
        var packet = networkPacket.Packet;

        if (networkPacket.NextHop >= networkPacket.Path.Length)
        {
            return false;
        }

        var next = networkPacket.Path[networkPacket.NextHop] as Network;

        if (next == null)
        {
            return false;
        }

        if (packet.Ttl <= 1)
        {
            packet.DecrementTtl();
            if (NetworkSimulator!.EnableMonitoring)
            {
                NetworkSimulator.Monitoring.Push(networkPacket.Packet.Id, NetworkSimulator.TotalTicks, this, next, packet.Payload, NetworkLoggerType.Unreachable,
                    $"NetworkMonitoringPacket unreachable: {packet.From} => {packet.To}", "Network", packet.Category, null);
            }
            //destination uneachable
            return false;
        }

        var nextNetworkPacket = new NetworkPacketSlot(NetworkSimulator!.TotalTicks,
            _networkTypeOption.TransferTicks, passTillNextTick: true, packet,
            networkPacket.Path, networkPacket.NextHop + 1, networkPacket.Type, networkPacket.DestinationNode);
        if (!next.TransferNext(ref nextNetworkPacket))
        {
            deferred = true;
            return false;
        }

        if (NetworkSimulator!.EnableMonitoring)
        {
            NetworkSimulator.Monitoring.Push(networkPacket.Packet.Id, NetworkSimulator.TotalTicks, this, next, packet.Payload, NetworkLoggerType.Transfer,
                $"Push netwok packet:  {this.Name} => {next.Name}",
                "Network", packet.Category, null, packet.Ttl, queueLength: _monitoringPacketsQueue.Count);
        }


        _networkCounters.CountTransfers();
        _networkCounters.CountOutbound(networkPacket.Packet);

        return true;
    }

    protected override bool ReceiveImplementation(INetworkPacket packet)
    {
        return true;
    }

    //TODO: add tick reaction
    public override void Refresh()
    {
        // Ready packets (snapshot) are processed first, then packets deferred on
        // previous ticks. Not-yet-due packets move to a plain list instead of
        // being re-enqueued into the concurrent queue every tick; each packet is
        // decremented at most once per tick and the processing order matches the
        // previous single-queue pass.
        var deferredNext = _deferredPacketsNext;
        var totalTicks = NetworkSimulator!.TotalTicks;

        // Expire the throughput window before the node phase can enqueue new
        // packets. Sends only claim budget; the prune stays single-threaded.
        _admission.Prune(totalTicks);

        var pendingCount = Volatile.Read(ref _queueLength);
        for (var i = 0; i < pendingCount && _monitoringPacketsQueue.TryDequeue(out var pendingPacket); i++)
        {
            Interlocked.Decrement(ref _queueLength);
            if (!ProcessNetworkPacket(ref pendingPacket, totalTicks))
            {
                deferredNext.Add(pendingPacket);
            }
        }

        var deferred = _deferredPackets;
        for (var i = 0; i < deferred.Count; i++)
        {
            var pendingPacket = deferred[i];
            if (!ProcessNetworkPacket(ref pendingPacket, totalTicks))
            {
                deferredNext.Add(pendingPacket);
            }
        }
        deferred.Clear();

        _deferredPackets = deferredNext;
        _deferredPacketsNext = deferred;

        if (NetworkSimulator!.EnableCounters)
        {
            _networkCounters.SetQueueLength(Volatile.Read(ref _queueLength) + _deferredPackets.Count + _deferredPacketsNext.Count);
            Counters.Refresh(NetworkSimulator!.TotalTicks, NetworkSimulator.CurrentRefreshSteps);
        }
    }

    /// <returns>true when the packet is fully processed; false when it must stay deferred.</returns>
    private bool ProcessNetworkPacket(ref NetworkPacketSlot pendingPacket, long totalTicks)
    {
        if (pendingPacket.PassTillNextTick && pendingPacket.CreatedTick == totalTicks)
        {
            INetwork? to = pendingPacket.NextHop < pendingPacket.Path.Length
                ? pendingPacket.Path[pendingPacket.NextHop]
                : null;
            if (to == null || !NetworkSimulator!.EnableMonitoring)
            {
                return false;
            }

            NetworkSimulator.Monitoring.Push(pendingPacket.Packet.Id, NetworkSimulator.TotalTicks,
                this, to,
                pendingPacket.Packet.Payload, NetworkLoggerType.Pass,
                $"Pass incomming: {pendingPacket.Packet.From} -> {to!.Name}",
                pendingPacket.Packet.Protocol, "Node");
            return false;
        }

        pendingPacket.ReduceTransferTicks();
        if (pendingPacket.TransferWaitTicks <= 0 && !pendingPacket.Released)
        {
            ProcessTransferPacket(pendingPacket, out var deferred);
            return !deferred;
        }

        return false;
    }


    //TODO: need VIRTUAL wait 
    private bool ProcessTransferPacket(NetworkPacketSlot networkPacket, out bool deferred)
    {
        deferred = false;
        var result = false;
        var packet = networkPacket.Packet;
        var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref packet!,
            $"Transfer packet {packet.From} to {packet.To}");

        if (networkPacket.Type == NetworkPacketType.Local)
        {
            result = SendToLocal(this, networkPacket);
        }
        else if (networkPacket.Type == NetworkPacketType.Remote)
        {
            result = SendToRemote(networkPacket, out deferred);
        }
        return result;
    }

    public INode? FindNode(string address, NodeType type)
    {
        return NetworkSimulator!.GetNode(address, type);
    }

    public override string ToString()
    {
        return $"N: {Address}";
    }

    internal ISender? GetDestinationNode(string id, NodeType destinationNodeType)
    {
        ISender? result = null;
        switch (destinationNodeType)
        {
            case NodeType.Network:
                if (id == Name)
                {
                    result = this;
                }
                break;
            case NodeType.Server:
                result = _servers.GetValueOrDefault(id);
                break;
            case NodeType.Client:
                result = _clients.GetValueOrDefault(id);
                break;
            default:
                break;
        }

        return result;
    }

    public bool AddApplication(IApplication application)
    {
        if (application == null) throw new ArgumentNullException(nameof(application));

        if (_applications.ContainsKey(application.Name))
        {
            return false;
        }
        _applications[application.Name] = application;

        ((ApplicationBase)application).Network = this;

        _nodesVersion++;

        return true;
    }

    public bool RemoveApplication(string application)
    {
        if (!_applications.ContainsKey(application))
        {
            return false;
        }

        _applications.Remove(application);

        _nodesVersion++;

        return true;
    }

    public override void Reset()
    {
        Clear();
    }

    public override void Clear()
    {
        lock (_admissionLock)
        {
            _monitoringPacketsQueue.Clear();
            Interlocked.Exchange(ref _queueLength, 0);
            _deferredPackets.Clear();
            _deferredPacketsNext.Clear();
            _admission.Reset();
        }
    }
}
