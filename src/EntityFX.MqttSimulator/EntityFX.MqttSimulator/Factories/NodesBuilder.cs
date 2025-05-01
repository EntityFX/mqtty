using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

public class NodesBuilder : INodesBuilder
{
    public NodesBuilder(
        Dictionary<string, IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>> clientFactory,
        Dictionary<string, IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>> serverFactory,
        Dictionary<string, IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>> applicationFactory,
    IFactory<INetwork?, NodeBuildOptions<NetworkBuildOption>> networkFactory
        )
    {
        ClientFactory = clientFactory;
        ServerFactory = serverFactory;
        NetworkFactory = networkFactory;
        ApplicationFactory = applicationFactory;
    }

    private INetwork? _network;

    private INetworkSimulator? _networkGraph;

    public Dictionary<string, IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>> ClientFactory { get; }

    public Dictionary<string, IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>> ServerFactory { get; }

    public IFactory<INetwork?, NodeBuildOptions<NetworkBuildOption>> NetworkFactory { get; }

    public Dictionary<string, IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>> ApplicationFactory { get; }

    public INodesBuilder WithNetwork(INetwork network)
    {
        _network = network;
        return this;
    }

    public INodesBuilder WithNetworkGraph(INetworkSimulator networkGraph)
    {
        _networkGraph = networkGraph;
        return this;
    }
}