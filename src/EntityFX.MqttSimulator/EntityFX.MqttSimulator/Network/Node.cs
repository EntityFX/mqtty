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
        new(_incommingMessages.Count, _outgoingMessages.Count, _responseMessages.Count);
    /// <summary>Overrides should combine base.IsQuiescent with their processing/handshake state.</summary>
    public virtual bool IsQuiescent =>
        _outgoingMessages.IsEmpty && _outgoingDeferred.Count == 0 && _outgoingDeferredNext.Count == 0 &&
        _incommingMessages.IsEmpty && _incommingDeferred.Count == 0 && _incommingDeferredNext.Count == 0;
    //TODO: NodePacket <- в нём декрементим время таймаута на ожидание
    //храним только Guid, ManualResetEventSlim
    private readonly ConcurrentDictionary<long, ResponseMonitoringPacket> _responseMessages = new();
    private long _responseRefreshCount;
    // MQTT publish handshakes have their own state machine; only legacy blocking
    // request APIs need the generic transport response table.
    protected virtual bool ShouldTrackResponse(INetworkPacket packet) => true;
    private readonly ConcurrentQueue<NodeMonitoringPacket> _outgoingMessages = new();
    private readonly ConcurrentQueue<NodeMonitoringPacket> _incommingMessages = new();

    // Deferred (not-yet-due) packets, split from the ready queues so each tick
    // only touches packets once and never re-enqueues the whole backlog
    // (O(ready) per tick instead of O(whole queue)).
    private List<NodeMonitoringPacket> _outgoingDeferred = new();
    private List<NodeMonitoringPacket> _outgoingDeferredNext = new();
    private List<NodeMonitoringPacket> _incommingDeferred = new();
    private List<NodeMonitoringPacket> _incommingDeferredNext = new();

    private static readonly ConcurrentBag<NodeMonitoringPacket> _packetPool = new();
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
        _outgoingDeferred.Clear();
        _outgoingDeferredNext.Clear();
        _incommingDeferred.Clear();
        _incommingDeferredNext.Clear();
        _responseMessages.Clear();
        _responseRefreshCount = 0;
    }

    public override void Refresh()
    {
        var sim = NetworkSimulator!;
        var totalTicks = sim.TotalTicks;

        ProcessReady(_outgoingMessages, _outgoingDeferred, _outgoingDeferredNext, totalTicks, SendToNetwork);
        ProcessReady(_incommingMessages, _incommingDeferred, _incommingDeferredNext, totalTicks, CompleteReceive);

        // Rotate the deferred lists so packets deferred this tick are processed
        // on the next tick (matches Network.Refresh swap).
        (_outgoingDeferred, _outgoingDeferredNext) = (_outgoingDeferredNext, _outgoingDeferred);
        (_incommingDeferred, _incommingDeferredNext) = (_incommingDeferredNext, _incommingDeferred);

        Interlocked.Increment(ref _responseRefreshCount);
        if (sim.EnableCounters)
        {
            base.Refresh();
            counters.SetOutgoingQueueLength(_outgoingMessages.Count + _outgoingDeferred.Count + _outgoingDeferredNext.Count);
            counters.SetIncommingQueueLength(_incommingMessages.Count + _incommingDeferred.Count + _incommingDeferredNext.Count);
            counters.SetReceiveQueueLength(_responseMessages.Count);
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
        ConcurrentQueue<NodeMonitoringPacket> queue,
        List<NodeMonitoringPacket> deferred,
        List<NodeMonitoringPacket> deferredNext,
        long totalTicks,
        Action<NodeMonitoringPacket> process)
    {
        var readyCount = queue.Count;
        for (var i = 0; i < readyCount && queue.TryDequeue(out var packet); i++)
        {
            if (!ProcessPacket(packet, totalTicks, process))
            {
                deferredNext.Add(packet);
            }
        }

        for (var i = 0; i < deferred.Count; i++)
        {
            var packet = deferred[i];
            if (!ProcessPacket(packet, totalTicks, process))
            {
                deferredNext.Add(packet);
            }
        }

        deferred.Clear();
    }

    private static bool ProcessPacket(
        NodeMonitoringPacket packet, long totalTicks, Action<NodeMonitoringPacket> process)
    {
        if (packet.PassTillNextTick && packet.Tick == totalTicks)
        {
            return false; // keep deferred until the next tick
        }

        packet.ReduceWaitTicks();
        if (packet.WaitTicks <= 0 && !packet.Released)
        {
            process(packet);
            return true;
        }

        return false;
    }

    private void SendToNetwork(NodeMonitoringPacket outgoing)
    {
        var packet = outgoing.RequestPacket;
        outgoing.Release();

        Network?.Send(packet);

        _packetPool.Add(outgoing);
    }

    private void CompleteReceive(NodeMonitoringPacket incomming)
    {
        var packet = incomming.RequestPacket;
        incomming.Release();
        CompleteReceiveImplementation(packet);
        _packetPool.Add(incomming);
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
        NodeMonitoringPacket outgoing;
        if (!_packetPool.TryTake(out outgoing!))
        {
            outgoing = new NodeMonitoringPacket(tick, packet, true, sim.WaitMode)
            {
                WaitTicks = TicksOptions.OutgoingWaitTicks,
                Id = sim.GetPacketId(),
                SendTick = tick
            };
        }
        else
        {
            outgoing.Reset(tick, packet, true, sim.WaitMode, TicksOptions.OutgoingWaitTicks, sim.GetPacketId(), tick);
        }
        if (ShouldTrackResponse(packet))
        _responseMessages[packet.Id] = new ResponseMonitoringPacket(sim.WaitMode, Volatile.Read(ref _responseRefreshCount))
        {
            RequestPacket = packet,
            RequestTick = outgoing.SendTick,
            Marker = packet.Category ?? string.Empty,
            Id = packet.Id
        };
        _outgoingMessages.Enqueue(outgoing);
    }

    private void PreReceive(INetworkPacket packet)
    {
        var sim = NetworkSimulator!;
        var tick = sim.TotalTicks;
        NodeMonitoringPacket incoming;
        if (!_packetPool.TryTake(out incoming!))
        {
            incoming = new NodeMonitoringPacket(tick, packet, true, sim.WaitMode)
            {
                WaitTicks = TicksOptions.OutgoingWaitTicks,
                Id = sim.GetPacketId(),
                SendTick = tick
            };
        }
        else
        {
            incoming.Reset(tick, packet, true, sim.WaitMode, TicksOptions.OutgoingWaitTicks, sim.GetPacketId(), tick);
        }
        _incommingMessages.Enqueue(incoming);
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

        _responseMessages.TryRemove(packetId, out _);

        if (monitorPacket.HasExpired(Volatile.Read(ref _responseRefreshCount)))
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
            var outgoingMessage = _outgoingMessages.FirstOrDefault(o => o.RequestPacket.Id == packetId);

            if (outgoingMessage == null)
            {
                return null;
            }

            var released = outgoingMessage.WaitIsReleased(TimeSpan.FromMinutes(1));

            if (released != true)
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

        _responseMessages.TryRemove(packetId, out _);

        if (isSet != true)
        {
            return null;
        }

        if (monitorPacket.HasExpired(Volatile.Read(ref _responseRefreshCount)))
        {
            return null;
        }

        return new ResponsePacket(
            monitorPacket.ResponsePacket!, monitorPacket.RequestTick,
            monitorPacket.ResponseTick ?? 0);
    }
}
