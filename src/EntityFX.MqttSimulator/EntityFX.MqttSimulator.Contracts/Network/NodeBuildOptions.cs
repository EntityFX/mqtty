namespace EntityFX.MqttY.Contracts.Network;

public class NodeBuildOptions<TOptions>
{
    public NodeBuildOptions(
        IServiceProvider serviceProvider,
        INetworkGraph networkGraph, INetwork? network,
        int index,
        string name, string address, string? group, int? groupAmount, 
        string protocol, string specification, string? connectsTo, TOptions? additional)
    {
        ServiceProvider = serviceProvider;
        NetworkGraph = networkGraph;
        Network = network;
        Index = index;
        Name = name;
        Group = group;
        GroupAmount = groupAmount;
        Address = address;
        Protocol = protocol;
        Specification = specification;
        ConnectsTo = connectsTo;
        Additional = additional;
    }

    public INetworkGraph NetworkGraph { get; init; }
    public IServiceProvider ServiceProvider { get; init; }
    public INetwork? Network { get; init;}
    public int Index { get; }
    public string Name { get; init; }
    
    public string? Group { get; init; }
    public int? GroupAmount { get; init; }
        
    public string? Address { get; init; }
        
    public string Protocol { get; init; }

    public string Specification { get; init; }

    public string? ConnectsTo { get; init; }

    public TOptions? Additional { get; init; }
    
    public string? OptionsPath { get; set; }
}