using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Counter;
using System.Collections.Concurrent;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using System.Net.Sockets;
using EntityFX.MqttY.Contracts.Network.Diagnostics;

public abstract class Node : NodeBase, IQuiescenceParticipant, INodeQueueDiagnostics
{
    public NodeQueueSnapshot GetQueueSnapshot() =>
        new(Volatile.Read(ref _incommingQueueLength), Volatile.Read(ref _outgoingQueueLength),
            Volatile.Read(ref _responseCount));
    /// <summary>Overrides should combine base.IsQuiescent with their processing/handshake state.</summary>
    public virtual bool IsQuiescent =>
        _outgoingMessages.IsEmpty && _outgoingDeferred.Count == 0 && _outgoingDeferredNext.Count == 0 &&
        _incommingMessages.IsEmpty && _incommingDeferred.Count == 0 && _incommingDeferredNext.Count == 0;

    /// <summary>True while the legacy blocking request API has a response timer pending.</summary>
    internal bool HasPendingResponse => Volatile.Read(ref _responseCount) != 0;
    //TODO: NodePacket <- в нём декрементим время таймаута на ожидание
    //храним только Guid, ManualResetEventSlim
    private readonly ConcurrentDictionary<long, ResponseMonitoringPacket> _responseMessages = new();
    // MQTT publish handshakes have their own state machine; only legacy blocking
    // request APIs need the generic transport response table.
    protected virtual bool ShouldTrackResponse(INetworkPacket packet) => true;
    private readonly ConcurrentQueue<NodePacketSlot> _outgoingMessages = new();
    private readonly ConcurrentQueue<NodePacketSlot> _incommingMessages = new();
    private int _outgoingQueueLength;
    private int _incommingQueueLength;
    private int _responseCount;
    // Response timeouts must advance on every Refresh even when the graph tick
    // does not advance (tests and node-level stepping rely on this).
    private long _refreshCount;

    // Deferred (not-yet-due) packets, split from the ready queues so each tick
    // only touches packets once and never re-enqueues the whole backlog
    // (O(ready) per tick instead of O(whole queue)).
    private List<NodePacketSlot> _outgoingDeferred = new();
    private List<NodePacketSlot> _outgoingDeferredNext = new();
    private List<NodePacketSlot> _incommingDeferred = new();
    private List<NodePacketSlot> _incommingDeferredNext = new();

    protected readonly TicksOptions TicksOptions;

    protected NodeCounters counters;
    private INetwork? network;

    public override CounterGroup Counters
    {
        get => counters;
        set
        {
            counters = (NodeCounters)value;

            if (network?.NetworkSimulator != null)
            {
                counters.Enabled = network!.NetworkSimulator!.EnableCounters;
            }
        }
    }

    public INetwork? Network
    {
        get => network; internal set
        {
            network = value;
            counters.Enabled = network!.NetworkSimulator!.EnableCounters;
        }
    }

    public Node(int index, string name, string address,
        TicksOptions ticksOptions, bool enableCounters) : base(index, name, address)
    {
        ticksOptions.Validate();
        TicksOptions = ticksOptions;
        counters = new NodeCounters(Name, "N", Group ?? "Node", "NG", TicksOptions.CounterHistoryDepth, enableCounters);
    }

    public override void Reset()
    {
        Clear();
    }

    public override void Clear()
    {
        if (counters != null)
        {
            counters.Clear();
        }
        _outgoingMessages.Clear();
        _incommingMessages.Clear();
        _outgoingQueueLength = 0;
        _incommingQueueLength = 0;
        _outgoingDeferred.Clear();
        _outgoingDeferredNext.Clear();
        _incommingDeferred.Clear();
        _incommingDeferredNext.Clear();
        _responseMessages.Clear();
        _responseCount = 0;
        _refreshCount = 0;
    }

    public override void Refresh()
    {
        var sim = NetworkSimulator!;
        var totalTicks = sim.TotalTicks;
        _refreshCount++;

        ProcessReady(_outgoingMessages, _outgoingDeferred, _outgoingDeferredNext, totalTicks, SendToNetwork,
            ref _outgoingQueueLength);
        ProcessReady(_incommingMessages, _incommingDeferred, _incommingDeferredNext, totalTicks, CompleteReceive,
            ref _incommingQueueLength);

        // Rotate the deferred lists so packets deferred this tick are processed
        // on the next tick (matches Network.Refresh swap).
        (_outgoingDeferred, _outgoingDeferredNext) = (_outgoingDeferredNext, _outgoingDeferred);
        (_incommingDeferred, _incommingDeferredNext) = (_incommingDeferredNext, _incommingDeferred);

        if (sim.EnableCounters)
        {
            base.Refresh();
            counters.SetOutgoingQueueLength(Volatile.Read(ref _outgoingQueueLength) + _outgoingDeferred.Count + _outgoingDeferredNext.Count);
            counters.SetIncommingQueueLength(Volatile.Read(ref _incommingQueueLength) + _incommingDeferred.Count + _incommingDeferredNext.Count);
            // ConcurrentDictionary.Count acquires every bucket lock, which made
            // the empty response table the most expensive part of a node tick.
            counters.SetReceiveQueueLength(Volatile.Read(ref _responseCount));
        }
    }

    /// <summary>
    /// Processes the ready snapshot of <paramref name="queue"/> followed by the
    /// deferred list from previous ticks. A packet is decremented at most once
    /// per tick; not-yet-due packets move to the next-tick deferred list without
    /// re-enqueue churn. Processing order matches the previous single-queue pass
    /// (ready items first, then previously deferred ones).
    /// </summary>
    private static void ProcessReady(
        ConcurrentQueue<NodePacketSlot> queue,
        List<NodePacketSlot> deferred,
        List<NodePacketSlot> deferredNext,
        long totalTicks,
        Action<NodePacketSlot> process,
        ref int queueLength)
    {
        var readyCount = Volatile.Read(ref queueLength);
        for (var i = 0; i < readyCount && queue.TryDequeue(out var packet); i++)
        {
            Interlocked.Decrement(ref queueLength);
            if (!ProcessSlot(ref packet, totalTicks, process))
            {
                deferredNext.Add(packet);
            }
        }

        for (var i = 0; i < deferred.Count; i++)
        {
            var packet = deferred[i];
            if (!ProcessSlot(ref packet, totalTicks, process))
            {
                deferredNext.Add(packet);
            }
        }

        deferred.Clear();
    }

    private static bool ProcessSlot(
        ref NodePacketSlot slot, long totalTicks, Action<NodePacketSlot> process)
    {
        if (slot.PassTillNextTick && slot.CreatedTick == totalTicks)
        {
            return false; // keep deferred until the next tick
        }

        if (slot.WaitTicks > 0)
        {
            slot.WaitTicks--;
        }

        if (slot.WaitTicks <= 0 && !slot.Released)
        {
            process(slot);
            return true;
        }

        return false;
    }

    private void SendToNetwork(NodePacketSlot outgoing)
    {
        var packet = outgoing.Packet;
        outgoing.Release();

        Network?.Send(packet);
    }

    private void CompleteReceive(NodePacketSlot incomming)
    {
        var packet = incomming.Packet;
        incomming.Release();
        CompleteReceiveImplementation(packet);
    }

    protected override bool SendImplementation(INetworkPacket packet)
    {
        PreSend(packet);
        if (NetworkSimulator!.EnableCounters) counters.SendCounter.Increment();
        return true;
    }

    protected override bool ReceiveImplementation(INetworkPacket packet)
    {
        PreReceive(packet);
        counters.ReceiveCounter.Increment();
        return true;
    }

    protected virtual bool CompleteReceiveImplementation(INetworkPacket packet)
    {
        if (packet.RequestId == null)
        {
            return false;
        }

        _responseMessages.TryGetValue(packet.RequestId.Value, out var monitorMessage);

        if (monitorMessage == null)
        {
            return false;
        }

        monitorMessage.ResponsePacket = packet;
        monitorMessage.ResponseTick = NetworkSimulator!.TotalTicks;
        monitorMessage.Receive();

        return true;
    }


    private void PreSend(INetworkPacket packet)
    {
        var sim = NetworkSimulator!;
        var tick = sim.TotalTicks;
        var outgoing = new NodePacketSlot(tick, packet, passTillNextTick: true, sim.WaitMode,
            TicksOptions.OutgoingWaitTicks, sim.GetPacketId());
        if (ShouldTrackResponse(packet))
        {
            _responseMessages[packet.Id] = new ResponseMonitoringPacket(sim.WaitMode, _refreshCount)
            {
                RequestPacket = packet,
                RequestTick = outgoing.SendTick,
                Marker = packet.Category ?? string.Empty,
                Id = packet.Id
            };
            Interlocked.Increment(ref _responseCount);
        }
        _outgoingMessages.Enqueue(outgoing);
        Interlocked.Increment(ref _outgoingQueueLength);
    }

    private void PreReceive(INetworkPacket packet)
    {
        var sim = NetworkSimulator!;
        var tick = sim.TotalTicks;
        var incoming = new NodePacketSlot(tick, packet, passTillNextTick: true, sim.WaitMode,
            TicksOptions.OutgoingWaitTicks, sim.GetPacketId());
        _incommingMessages.Enqueue(incoming);
        Interlocked.Increment(ref _incommingQueueLength);
    }

    protected ResponsePacket? WaitResponse(long packetId)
    {
        return NetworkSimulator!.WaitMode ? WaitMonitorResponse(packetId) : WaitNoMonitorResponse(packetId);
    }


    private ResponsePacket? WaitNoMonitorResponse(long packetId)
    {
        _responseMessages.TryGetValue(packetId, out var monitorPacket);

        if (monitorPacket == null)
        {
            return null;
        }

        if (_responseMessages.TryRemove(packetId, out _))
        {
            Interlocked.Decrement(ref _responseCount);
        }

        if (monitorPacket.HasExpired(_refreshCount))
        {
            return null;
        }

        return new ResponsePacket(
            monitorPacket.ResponsePacket!, monitorPacket.RequestTick,
            monitorPacket.ResponseTick ?? 0);
    }

    private ResponsePacket? WaitMonitorResponse(long packetId)
    {
        _responseMessages.TryGetValue(packetId, out var monitorPacket);

        if (monitorPacket == null)
        {
            var outgoingMessage = _outgoingMessages.FirstOrDefault(o => o.Packet != null && o.Packet.Id == packetId);

            if (outgoingMessage.Packet == null)
            {
                return null;
            }

            var released = outgoingMessage.WaitIsReleased(TimeSpan.FromMinutes(1));

            if (!released)
            {
                return null;
            }

            _responseMessages.TryGetValue(packetId, out monitorPacket);

            if (monitorPacket == null)
            {
                return null;
            }
        }

        var isSet = monitorPacket.WaitIsSet(TimeSpan.FromMinutes(1));

        if (_responseMessages.TryRemove(packetId, out _))
        {
            Interlocked.Decrement(ref _responseCount);
        }

        if (isSet != true)
        {
            return null;
        }

        if (monitorPacket.HasExpired(_refreshCount))
        {
            return null;
        }

        return new ResponsePacket(
            monitorPacket.ResponsePacket!, monitorPacket.RequestTick,
            monitorPacket.ResponseTick ?? 0);
    }
}
