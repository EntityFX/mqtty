using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;

public abstract class NodeBase : ISender
{
    protected readonly INetworkGraph NetworkGraph;

    public Guid Id { get; private set; }
    
    public int Index { get; private set; }

    public string Address { get; private set; }

    public string Name { get; private set; }
    
    public string? Group { get; set; }

    public abstract NodeType NodeType { get; }
    public int? GroupAmount { get; set; }
    public MonitoringScope? Scope { get; set; }

    public abstract Task<Packet> ReceiveWithResponseAsync(Packet packet);

    public abstract Task<Packet> SendWithResponseAsync(Packet packet);

    public abstract Task SendAsync(Packet packet);

    public abstract Task ReceiveAsync(Packet packet);

    protected abstract void BeforeReceive(Packet packet);
    protected abstract void AfterReceive(Packet packet);

    protected abstract void BeforeSend(Packet packet);
    protected abstract void AfterSend(Packet packet);

    public NodeBase(int index, string name, string address, INetworkGraph networkGraph)
    {
        Address = address;
        Name = name;
        Id = Guid.NewGuid();
        Index = index;
        this.NetworkGraph = networkGraph;
    }

    protected Packet GetPacket(string to, NodeType toType, byte[] payload, string protocol = null, string? category = null)
        => new Packet(Name, to, NodeType, toType, payload, protocol, category);

    public virtual void Refresh()
    {
        Tick();
    }

    public virtual void Tick()
    {
        NetworkGraph.Tick(this);
    }
}
