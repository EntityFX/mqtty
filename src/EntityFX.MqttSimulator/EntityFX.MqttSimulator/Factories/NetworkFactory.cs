using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

internal class NetworkFactory : IFactory<INetwork, NodeBuildOptions<NetworkBuildOption>>
{
    public INetwork Configure(
        NodeBuildOptions<NetworkBuildOption> options,
        INetwork service)
    {
        return service;
    }

    public INetwork Create(
        NodeBuildOptions<NetworkBuildOption> options)
    {
        var network = new Network.Network(options.Index, 
            options.Name, 
            options.Address ?? options.Name,
            options.Additional!.NetworkTypeOption!.NetworkType,
            options.Additional.NetworkTypeOption,
            options.Additional!.TicksOptions!, options.Additional!.EnableCounters);

        options.NetworkGraph.AddNetwork(network);

        return network;
    }
}
