using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Counter;
using System.Collections.Concurrent;
using EntityFX.MqttY.Network;

public abstract class Node : NodeBase
{
    //TODO: NodePacket <- в нём декрементим время таймаута на ожидание
    //храним только Guid, ManualResetEventSlim
    private readonly Dictionary<Guid, ResponseMonitoringPacket> _responseMessages = new();
    private readonly List<NodeMonitoringPacket> _outgoingMessages = new();
    private readonly List<NodeMonitoringPacket> _incommingMessages = new();
    protected readonly TicksOptions TicksOptions;

    protected NodeCounters counters;

    public override CounterGroup Counters
    {
        get => counters;
        set
        {
            counters = (NodeCounters)value;
        }
    }

    public INetwork? Network { get; internal set; }

    public Node(int index, string name, string address,
        TicksOptions ticksOptions) : base(index, name, address)
    {
        TicksOptions = ticksOptions;
        counters = new NodeCounters(Name, Group ?? "Node", TicksOptions.CounterHistoryDepth);
    }

    public override void Reset()
    {
        _outgoingMessages.Clear();
        _incommingMessages.Clear();
        _responseMessages.Clear();
    }

    public override void Refresh()
    {
        var outgoing = _outgoingMessages.ToArray();
        foreach (var outgoingMonitoringPacket in outgoing)
        {
            outgoingMonitoringPacket.ReduceWaitTicks();

            if (outgoingMonitoringPacket.WaitTicks <= 0 && !outgoingMonitoringPacket.Released)
            {
                SendToNetwork(outgoingMonitoringPacket);
            }
        }
        _outgoingMessages.RemoveAll(o => o.Released);

        var incomming = _incommingMessages.ToArray();
        foreach (var incommingMonitoringPacket in incomming)
        {
            incommingMonitoringPacket.ReduceWaitTicks();

            if (incommingMonitoringPacket.WaitTicks <= 0 && !incommingMonitoringPacket.Released)
            {
                CompleteReceive(incommingMonitoringPacket);
            }
        }
        _incommingMessages.RemoveAll(o => o.Released);


        foreach (var packet in _responseMessages)
        {
            packet.Value.ReduceWaitTicks();
        }
        base.Refresh();
        counters.SetOutgoingQueueLength(_outgoingMessages.Count);
        counters.SetIncommingQueueLength(_incommingMessages.Count);
        counters.SetReceiveQueueLength(_responseMessages.Count);
    }

    private void SendToNetwork(NodeMonitoringPacket outgoing)
    {
        var packet = outgoing.RequestPacket;
        _responseMessages[packet.Id] = new ResponseMonitoringPacket(NetworkSimulator!.WaitMode)
        {
            RequestPacket = packet,
            RequestTick = outgoing.SendTick,
            Marker = packet.Category ?? string.Empty,
            Id = packet.Id
        };
        outgoing.Release();

        Network?.Send(packet);
    }

    private void CompleteReceive(NodeMonitoringPacket incomming)
    {
        var packet = incomming.RequestPacket;
        incomming.Release();
        CompleteReceiveImplementation(packet);
    }

    protected override bool SendImplementation(INetworkPacket packet)
    {
        PreSend(packet);
        counters.SendCounter.Increment();
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

        var monitorMessage = _responseMessages.GetValueOrDefault(packet.RequestId.Value);

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
        _outgoingMessages.Add(new NodeMonitoringPacket(packet, NetworkSimulator!.WaitMode)
        {
            WaitTicks = TicksOptions.OutgoingWaitTicks,
            Id = Guid.NewGuid(),
            SendTick = NetworkSimulator!.TotalTicks
        });
    }

    private void PreReceive(INetworkPacket packet)
    {
        _incommingMessages.Add(new NodeMonitoringPacket(packet, NetworkSimulator!.WaitMode)
        {
            WaitTicks = TicksOptions.OutgoingWaitTicks,
            Id = Guid.NewGuid(),
            SendTick = NetworkSimulator!.TotalTicks
        });
    }

    protected ResponsePacket? WaitResponse(Guid packetId)
    {
        return NetworkSimulator!.WaitMode ? WaitMonitorResponse(packetId) : WaitNoMonitorResponse(packetId);
    }


    private ResponsePacket? WaitNoMonitorResponse(Guid packetId)
    {
        var monitorPacket = _responseMessages.GetValueOrDefault(packetId);

        if (monitorPacket == null)
        {
            return null;
        }

        _responseMessages.Remove(packetId);

        if (monitorPacket.IsExpired == true)
        {
            return null;
        }

        return new ResponsePacket(
            monitorPacket.ResponsePacket!, monitorPacket.RequestTick,
            monitorPacket.ResponseTick ?? 0);
    }

    private ResponsePacket? WaitMonitorResponse(Guid packetId)
    {
        var monitorPacket = _responseMessages.GetValueOrDefault(packetId);

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

            monitorPacket = _responseMessages.GetValueOrDefault(packetId);

            if (monitorPacket == null)
            {
                return null;
            }
        }

        var isSet = monitorPacket.WaitIsSet(TimeSpan.FromMinutes(1));

        _responseMessages.Remove(packetId);

        if (isSet != true)
        {
            return null;
        }

        if (monitorPacket.IsExpired == true)
        {
            return null;
        }

        return new ResponsePacket(
            monitorPacket.ResponsePacket!, monitorPacket.RequestTick,
            monitorPacket.ResponseTick ?? 0);
    }
}
