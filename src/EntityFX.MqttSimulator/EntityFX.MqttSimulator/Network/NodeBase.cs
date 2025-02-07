// See https://aka.ms/new-console-template for more information
using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;

public abstract class NodeBase : INode
{
    protected readonly INetworkGraph networkGraph;

    public Guid Id { get; private set; }

    public string Address { get; private set; }

    public abstract NodeType NodeType { get; }

    public abstract Task ReceiveAsync(Packet packet);

    public abstract Task SendAsync(Packet packet);

    public NodeBase(string address, INetworkGraph networkGraph)
    {
        Address = address;
        Id = Guid.NewGuid();
        this.networkGraph = networkGraph;
    }
}
