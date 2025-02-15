namespace EntityFX.MqttY.Contracts.Network;

public class NodeBuildOptions
{
    public NodeBuildOptions(
        INetworkGraph networkGraph, INetwork? network,
        int index,
        string name, string address, string? group, string protocol)
    {
        NetworkGraph = networkGraph;
        Network = network;
        Index = index;
        Name = name;
        Group = group;
        Address = address;
        Protocol = protocol;
    }

    public INetworkGraph NetworkGraph { get; init; }
    public INetwork? Network { get; init;}
    public int Index { get; }
    public string Name { get; init; }
    
    public string? Group { get; init; }
        
    public string? Address { get; init; }
        
    public string Protocol { get; init; }
}