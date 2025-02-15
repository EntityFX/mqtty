using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Mqtt;

namespace EntityFX.MqttY.Utils;

internal class ClientFactory : IFactory<IClient?, NodeBuildOptions>
{
    public IClient? Create(NodeBuildOptions options)
    {
        if (options.Network == null)
        {
            return null;
        }
        
        if (options.Protocol == "mqtt")
        {
            return new MqttClient(options.Index,
                options.Name, options.Address ?? options.Name, 
                options.Protocol, options.Network, options.NetworkGraph, options.Name);
        }

        return new Client(options.Index,options.Name, options.Address ?? options.Name, options.Protocol, 
            options.Network, options.NetworkGraph)
        {
            Group = options.Group
        };
    }
}