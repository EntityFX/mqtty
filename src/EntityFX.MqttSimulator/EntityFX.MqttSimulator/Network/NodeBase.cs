using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using System.Collections.Concurrent;
using System.Collections.Generic;

public abstract class NodeBase : ISender
{
    protected readonly INetworkGraph NetworkGraph;

    //TODO: NodePacket <- в нём декрементим время таймаута на ожидание
    //храним только Guid, ManualResetEventSlim
    private readonly ConcurrentDictionary<Guid, NodeMonitoringPacket> monitorMessages = new ConcurrentDictionary<Guid, NodeMonitoringPacket>();



    public Guid Id { get; private set; }

    public int Index { get; private set; }

    public string Address { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Group { get; set; }

    public abstract NodeType NodeType { get; }
    public int? GroupAmount { get; set; }
    public NetworkLoggerScope? Scope { get; set; }

    public abstract CounterGroup Counters { get; }


    protected abstract Task ReceiveImplementationAsync(NetworkPacket packet);

    protected abstract Task SendImplementationAsync(NetworkPacket packet);


    protected abstract void BeforeReceive(NetworkPacket packet);
    protected abstract void AfterReceive(NetworkPacket packet);

    protected abstract void BeforeSend(NetworkPacket packet);
    protected abstract void AfterSend(NetworkPacket packet);


    public NodeBase(int index, string name, string address, INetworkGraph networkGraph)
    {
        Address = address;
        Name = name;
        Id = Guid.NewGuid();
        Index = index;
        this.NetworkGraph = networkGraph;
    }


    //Создаём ManualResetEventSlim 
    public async Task SendAsync(NetworkPacket packet)
    {
        PreSend(packet);

        await SendImplementationAsync(packet);
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
            RequestTick = NetworkGraph.Monitoring.Ticks,
            Marker = packet.Category ?? string.Empty,
            Id = packet.Id,
            ResetEventSlim = new ManualResetEventSlim(false),
        }, (id, packet) => packet);
    }

    //Добавляем и Снимаем ManualResetEventSlim 
    public virtual async Task ReceiveAsync(NetworkPacket packet)
    {
        await ReceiveImplementationAsync(packet);

        if (packet.RequestId == null)
        {
            return;
        }

        var monitorMessage = monitorMessages.GetValueOrDefault(packet.RequestId.Value);

        if (monitorMessage == null)
        {
            return;
        }

        monitorMessage.ResponsePacket = packet;
        monitorMessage.ResponseTick = NetworkGraph.Monitoring.Ticks;
        monitorMessage.ResetEventSlim?.Set();
    }

    protected NetworkPacket GetPacket(Guid guid, string to, NodeType toType, byte[] payload,
        string protocol, string? category = null, Guid? requestId = null)
        => new NetworkPacket(Name, to, NodeType, toType, payload, protocol, category)
        {
            Id = guid,
            RequestId = requestId
        };


    //Здесь обновляем время ождидания и триггерим ManualResetEventSlim
    public virtual void Refresh()
    {
        foreach (var packet in monitorMessages.Values.ToArray())
        {
            packet.ReduceWaitTicks();

            if (packet.WaitTicks <= 0)
            {
                packet.ResetEventSlim?.Set();
            }
        }
    }

    public virtual void Reset()
    {

        foreach (var packet in monitorMessages.Values.ToArray())
        {
            packet.ResetEventSlim?.Set();
        }
        monitorMessages.Clear();
    }


    //подписываемся на ManualResetEventSlim и ждём его
    protected Task<ResponsePacket?> WaitResponse(Guid packetId)
    {
        return Task.Run(() =>
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
        });
    }
}
