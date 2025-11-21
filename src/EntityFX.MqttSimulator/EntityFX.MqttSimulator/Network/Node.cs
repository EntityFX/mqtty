using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Counter;
using System.Collections.Concurrent;

public abstract class Node : NodeBase
{
    //TODO: NodePacket <- в нём декрементим время таймаута на ожидание
    //храним только Guid, ManualResetEventSlim
    private readonly ConcurrentDictionary<Guid, NodeMonitoringPacket> _monitorMessages = new ConcurrentDictionary<Guid, NodeMonitoringPacket>();
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

    public Node(int index, string name, string address,
        TicksOptions ticksOptions) : base(index, name, address)
    {
        _ticksOptions = ticksOptions;
        counters = new NodeCounters(Name ?? string.Empty, _ticksOptions.CounterHistoryDepth);
    }

    public override void Reset()
    {
        foreach (var packet in _monitorMessages.Values.ToArray())
        {
            packet.ReceiveResetEventSlim?.Set();
        }
        _monitorMessages.Clear();
    }

    public override void Refresh()
    {
        foreach (var packet in _monitorMessages)
        {
            packet.Value.ReduceWaitTicks();

            if (packet.Value.WaitTicks <= 0)
            {
                packet.Value.ReceiveResetEventSlim?.Set();
                packet.Value.IsExpired = true;
            }
        }
        base.Refresh();
        counters.SetQueueLength(_monitorMessages.Count);
    }

    protected override void BeforeSend(NetworkPacket packet)
    {
        PreSend(packet);
    }

    protected override void AfterSend(NetworkPacket packet)
    {
        counters.SendCounter.Increment();
    }

    protected override void AfterReceive(NetworkPacket packet)
    {
        counters.ReceiveCounter.Increment();
    }

    protected override bool ReceiveImplementation(NetworkPacket packet)
    {
        if (packet.RequestId == null)
        {
            return false;
        }

        var monitorMessage = _monitorMessages.GetValueOrDefault(packet.RequestId.Value);

        if (monitorMessage == null)
        {
            return false;
        }

        monitorMessage.ResponsePacket = packet;
        monitorMessage.ResponseTick = NetworkSimulator.TotalTicks;
        monitorMessage.ReceiveResetEventSlim?.Set();
        monitorMessage.ReceiveIsSet = true;

        return true;
    }


    private void PreSend(NetworkPacket packet)
    {
        var startTicks = NetworkSimulator.TotalTicks;

        //while (NetworkGraph.TotalTicks - startTicks < _networkTypeOption.SendTicks)
        //{

        //}

        if (_monitorMessages.ContainsKey(packet.Id))
        {
            return;
        }

        _monitorMessages.AddOrUpdate(packet.Id, new NodeMonitoringPacket()
        {
            RequestPacket = packet,
            RequestTick = NetworkSimulator.TotalTicks,
            Marker = packet.Category ?? string.Empty,
            Id = packet.Id,
            ReceiveResetEventSlim = new ManualResetEventSlim(false),
        }, (id, packet) => packet);
    }

    //подписываемся на ManualResetEventSlim и ждём его
    protected ResponsePacket? WaitResponse(Guid packetId)
    {
        /*return Task.Run(() =>
        {*/
        var monitorPacket = _monitorMessages.GetValueOrDefault(packetId);

        if (monitorPacket == null)
        {
            return null;
        }

        //var isSet = monitorPacket?.ResetEventSlim?.Wait(TimeSpan.FromMinutes(1));
        var isSet = monitorPacket?.WaitIsSet(TimeSpan.FromMinutes(1));

        if (!_monitorMessages.TryRemove(packetId, out monitorPacket))
        {
            return null;
        }

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
        /* });*/
    }
}
