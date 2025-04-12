using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Counter;
using System.Collections.Concurrent;

public abstract class Node : NodeBase
{
    //TODO: NodePacket <- в нём декрементим время таймаута на ожидание
    //храним только Guid, ManualResetEventSlim
    private readonly ConcurrentDictionary<Guid, NodeMonitoringPacket> monitorMessages = new ConcurrentDictionary<Guid, NodeMonitoringPacket>();

    internal readonly NodeCounters counters;

    public override CounterGroup Counters => counters;

    public Node(int index, string name, string address, INetworkSimulator networkGraph) : base(index, name, address, networkGraph)
    {
        counters = new NodeCounters(Name ?? string.Empty);
    }

    public override void Reset()
    {
        foreach (var packet in monitorMessages.Values.ToArray())
        {
            packet.ResetEventSlim?.Set();
        }
        monitorMessages.Clear();
    }

    public override Task Refresh()
    {
        foreach (var packet in monitorMessages)
        {
            packet.Value.ReduceWaitTicks();

            if (packet.Value.WaitTicks <= 0)
            {
                packet.Value.ResetEventSlim?.Set();
            }
        }
        return base.Refresh();
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

    protected override Task ReceiveImplementationAsync(NetworkPacket packet)
    {
        if (packet.RequestId == null)
        {
            return Task.CompletedTask;
        }

        var monitorMessage = monitorMessages.GetValueOrDefault(packet.RequestId.Value);

        if (monitorMessage == null)
        {
            return Task.CompletedTask;
        }

        monitorMessage.ResponsePacket = packet;
        monitorMessage.ResponseTick = NetworkGraph.TotalTicks;
        monitorMessage.ResetEventSlim?.Set();

        return Task.CompletedTask;
    }


    private void PreSend(NetworkPacket packet)
    {
        if (monitorMessages.ContainsKey(packet.Id))
        {
            return;
        }

        monitorMessages.AddOrUpdate(packet.Id, new NodeMonitoringPacket()
        {
            RequestPacket = packet,
            RequestTick = NetworkGraph.TotalTicks,
            Marker = packet.Category ?? string.Empty,
            Id = packet.Id,
            ResetEventSlim = new ManualResetEventSlim(false),
        }, (id, packet) => packet);
    }

    //подписываемся на ManualResetEventSlim и ждём его
    protected Task<ResponsePacket?> WaitResponse(Guid packetId)
    {
        var monitorPacket = monitorMessages.GetValueOrDefault(packetId);

        if (monitorPacket == null)
        {
            return Task.FromResult<ResponsePacket?>(null);
        }

        var isSet = monitorPacket?.ResetEventSlim?.Wait(TimeSpan.FromMinutes(1));

        if (!monitorMessages.TryRemove(packetId, out monitorPacket))
        {
            return Task.FromResult<ResponsePacket?>(null);
        }

        if (isSet != true)
        {
            return Task.FromResult<ResponsePacket?>(null);
        }

        return Task.FromResult<ResponsePacket?>(new ResponsePacket(
            monitorPacket.ResponsePacket!, monitorPacket.RequestTick, monitorPacket.ResponseTick ?? 0));


        //return Task.Run(() =>
        //{
        //    var monitorPacket = monitorMessages.GetValueOrDefault(packetId);

        //    if (monitorPacket == null)
        //    {
        //        return Task.FromResult<ResponsePacket?>(null);
        //    }

        //    var isSet = monitorPacket?.ResetEventSlim?.Wait(TimeSpan.FromMinutes(1));

        //    if (!monitorMessages.TryRemove(packetId, out monitorPacket))
        //    {
        //        return Task.FromResult<ResponsePacket?>(null);
        //    }

        //    if (isSet != true)
        //    {
        //        return Task.FromResult<ResponsePacket?>(null);
        //    }

        //    return Task.FromResult<ResponsePacket?>(new ResponsePacket(
        //        monitorPacket.ResponsePacket!, monitorPacket.RequestTick, monitorPacket.ResponseTick ?? 0));
        //});
    }
}
