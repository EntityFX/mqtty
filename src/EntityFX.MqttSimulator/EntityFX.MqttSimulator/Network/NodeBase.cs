using EntityFX.MqttY.Contracts.Network;

public abstract class NodeBase : INode
{
    protected readonly INetworkGraph networkGraph;

    public Guid Id { get; private set; }

    public string Address { get; private set; }

    public string Name { get; private set; }

    public abstract NodeType NodeType { get; }

    public abstract Task<Packet> ReceiveAsync(Packet packet);

    public abstract Task<Packet> SendAsync(Packet packet);

    public NodeBase(string name, string address, INetworkGraph networkGraph)
    {
        Address = address;
        Name = name;
        Id = Guid.NewGuid();
        this.networkGraph = networkGraph;
    }
}
