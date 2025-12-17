using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Network;

public abstract class NodeBase : ISender
{
    public INetworkSimulator? NetworkSimulator { get; internal set; }

    public Guid Id { get; private set; }

    public int Index { get; private set; }

    public string Address { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Group { get; set; }

    public abstract NodeType NodeType { get; }
    public int? GroupAmount { get; set; }
    public NetworkLoggerScope? Scope { get; set; }

    public abstract CounterGroup Counters { get; set;  }


    protected abstract bool ReceiveImplementation(INetworkPacket packet);

    protected abstract bool SendImplementation(INetworkPacket packet);


    public NodeBase(int index, string name, string address)
    {
        Address = address;
        Name = name;
        Id = Guid.NewGuid();
        Index = index;
    }


    //Создаём ManualResetEventSlim 
    public bool Send(INetworkPacket packet)
    {
        NetworkSimulator!.Step();
        var result = SendImplementation(packet);

        return result;
    }



    //Добавляем и Снимаем ManualResetEventSlim 
    public bool Receive(INetworkPacket packet)
    {
        NetworkSimulator!.Step();
        var result = ReceiveImplementation(packet);

        return result;
    }

    protected INetworkPacket GetPacket(Guid guid, string to, NodeType toType, int toIndex,
        byte[] payload,
        string protocol, string? category = null, 
        Guid? requestId = null, int outgoingTicks = 1)
        => new NetworkPacket<int>(guid, requestId, Name, to, NodeType, toType, 
            Index, toIndex,
            payload, protocol, 0, outgoingTicks, Category: category)
        {
            Id = guid,
            RequestId = requestId,
            OutgoingTicks = outgoingTicks
        };

    protected NetworkPacket<TContext> GetContextPacket<TContext>(
        Guid guid, string to, NodeType toType,
        int toIndex, byte[] payload,
        string protocol, TContext context, string? category = null, 
        Guid? requestId = null, int outgoingTicks = 1)
        => new NetworkPacket<TContext>(guid, requestId, Name, to, NodeType, toType,
            Index, toIndex,
            payload, protocol, 0, outgoingTicks, Category: category, TypedContext: context)
        {
            Id = guid,
            RequestId = requestId,
            OutgoingTicks = outgoingTicks
        };

    //Здесь обновляем время ождидания и триггерим ManualResetEventSlim
    public virtual void Refresh()
    {
        if (NetworkSimulator == null) return;

        Counters.Refresh(NetworkSimulator.TotalTicks, NetworkSimulator.TotalSteps);
    }

    public abstract void Reset();
    public abstract void Clear();
}
