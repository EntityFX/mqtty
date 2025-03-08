using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Utils;

internal class NetworkFactory : IFactory<INetwork, NodeBuildOptions<Dictionary<string, string[]>>>
{
    public INetwork Configure(NodeBuildOptions<Dictionary<string, string[]>> options, INetwork service)
    {
        return service;
    }

    public INetwork Create(NodeBuildOptions<Dictionary<string, string[]>> options)
    {
        return new Network.Network(options.Index, options.Name, options.Address ?? options.Name, options.NetworkGraph);
    }
}