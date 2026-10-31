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
    public virtual bool IsQuiescent => _outgoingMessages.IsEmpty && _incommingMessages.IsEmpty;
    //TODO: NodePacket <- в нём декрементим время таймаута на ожидание
    //храним только Guid, ManualResetEventSlim
    private readonly ConcurrentDictionary<long, ResponseMonitoringPacket> _responseMessages = new();
    private long _responseRefreshCount;
    // MQTT publish handshakes have their own state machine; only legacy blocking
    // request APIs need the generic transport response table.
    protected virtual bool ShouldTrackResponse(INetworkPacket packet) => true;
    private readonly ConcurrentQueue<NodeMonitoringPacket> _outgoingMessages = new();
    private readonly ConcurrentQueue<NodeMonitoringPacket> _incommingMessages = new();
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
        _responseMessages.Clear();
        _responseRefreshCount = 0;
    }

    public override void Refresh()
    {
        var sim = NetworkSimulator!;
        var totalTicks = sim.TotalTicks;
        var outgoingCount = _outgoingMessages.Count;
        for (var i = 0; i < outgoingCount && _outgoingMessages.TryDequeue(out var outgoingMonitoringPacket); i++)
        {
            if (outgoingMonitoringPacket.PassTillNextTick && outgoingMonitoringPacket.Tick == totalTicks)
            {
                _outgoingMessages.Enqueue(outgoingMonitoringPacket);
                continue;
            }
            outgoingMonitoringPacket.ReduceWaitTicks();
            if (outgoingMonitoringPacket.WaitTicks <= 0 && !outgoingMonitoringPacket.Released)
            {
                SendToNetwork(outgoingMonitoringPacket);
            }
            else _outgoingMessages.Enqueue(outgoingMonitoringPacket);
        }

        var incomingCount = _incommingMessages.Count;
        for (var i = 0; i < incomingCount && _incommingMessages.TryDequeue(out var incommingMonitoringPacket); i++)
        {
            if (incommingMonitoringPacket.PassTillNextTick && incommingMonitoringPacket.Tick == totalTicks)
            {
                _incommingMessages.Enqueue(incommingMonitoringPacket);
                continue;
            }
            incommingMonitoringPacket.ReduceWaitTicks();
            if (incommingMonitoringPacket.WaitTicks <= 0 && !incommingMonitoringPacket.Released)
            {
                CompleteReceive(incommingMonitoringPacket);
            }
            else _incommingMessages.Enqueue(incommingMonitoringPacket);
        }

        Interlocked.Increment(ref _responseRefreshCount);
        if (sim.EnableCounters)
        {
            base.Refresh();
            counters.SetOutgoingQueueLength(_outgoingMessages.Count);
            counters.SetIncommingQueueLength(_incommingMessages.Count);
            counters.SetReceiveQueueLength(_responseMessages.Count);
        }
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
