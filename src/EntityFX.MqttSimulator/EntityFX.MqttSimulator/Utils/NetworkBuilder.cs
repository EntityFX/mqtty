using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Utils;

internal class NetworkBuilder : INetworkBuilder
{
    public NetworkBuilder(
        IFactory<IClient?, NodeBuildOptions> clientFactory, 
        IFactory<IServer?, NodeBuildOptions> serverFactory, 
        IFactory<INetwork?, NodeBuildOptions> networkFactory)
    {
        ClientFactory = clientFactory;
        ServerFactory = serverFactory;
        NetworkFactory = networkFactory;
    }

    private INetwork? _network;
    
    private INetworkGraph? _networkGraph;
    
    public IFactory<IClient?, NodeBuildOptions> ClientFactory { get; }
    
    public IFactory<IServer?, NodeBuildOptions> ServerFactory { get; }
    
    public IFactory<INetwork?, NodeBuildOptions> NetworkFactory { get; }
    


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