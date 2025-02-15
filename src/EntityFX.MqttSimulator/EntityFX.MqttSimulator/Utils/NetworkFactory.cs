using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Utils;

internal class NetworkFactory : IFactory<INetwork?, NodeBuildOptions>
{
    public INetwork? Create(NodeBuildOptions options)
    {
        return  new Network(options.Name, options.Address ?? options.Name, options.NetworkGraph);
    }
}