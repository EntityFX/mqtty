using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using System.Collections.Concurrent;
using System.Collections.Generic;

public abstract class NodeBase : ISender
{
    protected readonly INetworkGraph NetworkGraph;

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
        BeforeSend(packet);

        await SendImplementationAsync(packet);

        AfterSend(packet);
    }



    //Добавляем и Снимаем ManualResetEventSlim 
    public async Task ReceiveAsync(NetworkPacket packet)
    {
        BeforeReceive(packet);

        await ReceiveImplementationAsync(packet);

        AfterReceive(packet);

        if (packet.RequestId == null)
        {
            return;
        }
    }

    protected NetworkPacket GetPacket(Guid guid, string to, NodeType toType, byte[] payload,
        string protocol, string? category = null, Guid? requestId = null)
        => new NetworkPacket(Name, to, NodeType, toType, payload, protocol, category)
        {
            Id = guid,
            RequestId = requestId
        };


    //Здесь обновляем время ождидания и триггерим ManualResetEventSlim
    public virtual Task Refresh()
    {
        Counters.Refresh(NetworkGraph.TotalTicks);

        return Task.CompletedTask;
    }

    public abstract void Reset();
}
