using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Mqtt;

namespace EntityFX.MqttY.Utils;

internal class ServerFactory : IFactory<IServer?, NodeBuildOptions>
{
    public IServer? Create(NodeBuildOptions options)
    {
        if (options.Protocol == "mqtt")
        {
            return new MqttBroker
            (options.Name, options.Address ?? options.Name, 
                options.Protocol, options.Network, options.NetworkGraph);
        }

        return new Server(options.Name, options.Address ?? options.Name, 
            options.Protocol, options.Network, options.NetworkGraph);
    }
}