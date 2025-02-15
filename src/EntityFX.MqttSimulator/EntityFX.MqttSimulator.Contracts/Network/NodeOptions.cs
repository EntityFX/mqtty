namespace EntityFX.MqttY.Contracts.Network;

public class NodeBuildOptions
{
    public NodeBuildOptions(
        INetworkGraph networkGraph, INetwork network,
        string name, string address, string protocol)
    {
        NetworkGraph = networkGraph;
        Network = network;
        Name = name;
        Address = address;
        Protocol = protocol;
    }

    public INetworkGraph NetworkGraph { get; init; }
    public INetwork Network { get; init;}
    public string Name { get; init; }
        
    public string? Address { get; init; }
        
    public string Protocol { get; init; }
}