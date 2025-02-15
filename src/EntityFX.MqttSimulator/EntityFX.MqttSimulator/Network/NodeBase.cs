using EntityFX.MqttY.Contracts.Network;

public abstract class NodeBase : INode
{
    protected readonly INetworkGraph NetworkGraph;

    public Guid Id { get; private set; }
    
    public int Index { get; private set; }

    public string Address { get; private set; }

    public string Name { get; private set; }
    
    public string? Group { get; set; }

    public abstract NodeType NodeType { get; }

    public abstract Task<Packet> ReceiveAsync(Packet packet);

    public abstract Task<Packet> SendAsync(Packet packet);

    public NodeBase(int index, string name, string address, INetworkGraph networkGraph)
    {
        Address = address;
        Name = name;
        Id = Guid.NewGuid();
        Index = index;
        this.NetworkGraph = networkGraph;
    }
}
