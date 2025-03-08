using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Utils;

internal class NetworkBuilder : INetworkBuilder
{
    public NetworkBuilder(
        IFactory<IClient?, NodeBuildOptions<Dictionary<string, string[]>>> clientFactory,
        IFactory<IServer?, NodeBuildOptions<Dictionary<string, string[]>>> serverFactory,
        IFactory<INetwork?, NodeBuildOptions<Dictionary<string, string[]>>> networkFactory,
        IFactory<IApplication?, NodeBuildOptions<object>> applicationFactory)
    {
        ClientFactory = clientFactory;
        ServerFactory = serverFactory;
        NetworkFactory = networkFactory;
        ApplicationFactory = applicationFactory;
    }

    private INetwork? _network;
    
    private INetworkGraph? _networkGraph;
    
    public IFactory<IClient?,  NodeBuildOptions<Dictionary<string, string[]>>> ClientFactory { get; }
    
    public IFactory<IServer?, NodeBuildOptions<Dictionary<string, string[]>>> ServerFactory { get; }
    
    public IFactory<INetwork?,  NodeBuildOptions<Dictionary<string, string[]>>> NetworkFactory { get; }

    public IFactory<IApplication?, NodeBuildOptions<object>> ApplicationFactory { get; }

    public INetworkBuilder WithNetwork(INetwork network)
    {
        _network = network;
        return this;
    }

    public INetworkBuilder WithNetworkGraph(INetworkGraph networkGraph)
    {
        _networkGraph = networkGraph;
        return this;
    }
}