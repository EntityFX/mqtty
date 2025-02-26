using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Utils;

internal class NetworkBuilder : INetworkBuilder
{
    public NetworkBuilder(
        IFactory<IClient?, Dictionary<string, string[]>> clientFactory,
        IFactory<IServer?, Dictionary<string, string[]>> serverFactory,
        IFactory<INetwork?, Dictionary<string, string[]>> networkFactory,
        IFactory<IApplication?, object> applicationFactory)
    {
        ClientFactory = clientFactory;
        ServerFactory = serverFactory;
        NetworkFactory = networkFactory;
        ApplicationFactory = applicationFactory;
    }

    private INetwork? _network;
    
    private INetworkGraph? _networkGraph;
    
    public IFactory<IClient?,  Dictionary<string, string[]>> ClientFactory { get; }
    
    public IFactory<IServer?, Dictionary<string, string[]>> ServerFactory { get; }
    
    public IFactory<INetwork?,  Dictionary<string, string[]>> NetworkFactory { get; }

    public IFactory<IApplication?, object> ApplicationFactory { get; }

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