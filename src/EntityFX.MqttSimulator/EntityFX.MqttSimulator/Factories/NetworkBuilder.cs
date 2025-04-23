using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

internal class NetworkBuilder : INetworkBuilder
{
    public NetworkBuilder(
        IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>> clientFactory,
        IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>> serverFactory,
        IFactory<INetwork?, NodeBuildOptions<NetworkBuildOption>> networkFactory,
        IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>> applicationFactory)
    {
        ClientFactory = clientFactory;
        ServerFactory = serverFactory;
        NetworkFactory = networkFactory;
        ApplicationFactory = applicationFactory;
    }

    private INetwork? _network;

    private INetworkSimulator? _networkGraph;

    public IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>> ClientFactory { get; }

    public IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>> ServerFactory { get; }

    public IFactory<INetwork?, NodeBuildOptions<NetworkBuildOption>> NetworkFactory { get; }

    public IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>> ApplicationFactory { get; }

    public INetworkBuilder WithNetwork(INetwork network)
    {
        _network = network;
        return this;
    }

    public INetworkBuilder WithNetworkGraph(INetworkSimulator networkGraph)
    {
        _networkGraph = networkGraph;
        return this;
    }
}