namespace EntityFX.MqttY.Contracts.Network;

public class NodeBuildOptions<TOptions>
{
    public NodeBuildOptions(
        INetworkGraph networkGraph, INetwork? network,
        int index,
        string name, string address, string? group, int? groupAmount, string protocol, string? connectsTo, TOptions? additional)
    {
        NetworkGraph = networkGraph;
        Network = network;
        Index = index;
        Name = name;
        Group = group;
        GroupAmount = groupAmount;
        Address = address;
        Protocol = protocol;
        ConnectsTo = connectsTo;
        Additional = additional;
    }

    public INetworkGraph NetworkGraph { get; init; }
    public INetwork? Network { get; init;}
    public int Index { get; }
    public string Name { get; init; }
    
    public string? Group { get; init; }
    public int? GroupAmount { get; init; }
        
    public string? Address { get; init; }
        
    public string Protocol { get; init; }

    public string? ConnectsTo { get; init; }

    public TOptions? Additional { get; init; }
}