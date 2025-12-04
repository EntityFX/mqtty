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
    private readonly List<OutgoingMonitoringPacket> _outgoingMessages = new();
    private readonly TicksOptions _ticksOptions;

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
        _ticksOptions = ticksOptions;
        counters = new NodeCounters(Name, Group ?? "Node", _ticksOptions.CounterHistoryDepth);
    }

    public override void Reset()
    {
        _outgoingMessages.Clear();
        _responseMessages.Clear();
    }

    public override void Refresh()
    {
        var outgoing = _outgoingMessages.ToArray();
        foreach (var outgoingMonitoringPacket in outgoing)
        {
            outgoingMonitoringPacket.ReduceDelayTicks();

            if (outgoingMonitoringPacket.DelayTicks <= 0 && !outgoingMonitoringPacket.Released)
            {
                SendToNetwork(outgoingMonitoringPacket);
            }
        }

        _outgoingMessages.RemoveAll(o => o.Released);


        foreach (var packet in _responseMessages)
        {
            packet.Value.ReduceWaitTicks();
        }
        base.Refresh();
        counters.SetQueueLength(_responseMessages.Count);
    }

    private void SendToNetwork(OutgoingMonitoringPacket outgoing)
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

    protected override void BeforeSend(INetworkPacket packet)
    {
        PreSend(packet);
    }

    protected override bool SendImplementation(INetworkPacket packet)
    {
        return true;
    }

    protected override void AfterSend(INetworkPacket packet)
    {
        counters.SendCounter.Increment();
    }

    protected override void AfterReceive(INetworkPacket packet)
    {
        counters.ReceiveCounter.Increment();
    }

    protected override bool ReceiveImplementation(INetworkPacket packet)
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
        _outgoingMessages.Add(new OutgoingMonitoringPacket(packet, NetworkSimulator!.WaitMode)
        {
            DelayTicks = 2,
            Id = Guid.NewGuid(),
            SendTick = NetworkSimulator!.TotalTicks
        });

        // if (_responseMessages.ContainsKey(packet.Id))
        // {
        //     return;
        // }
        //

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
