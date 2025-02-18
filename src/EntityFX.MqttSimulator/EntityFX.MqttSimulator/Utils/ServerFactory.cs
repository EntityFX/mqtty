using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Mqtt;

namespace EntityFX.MqttY.Utils;

internal class ServerFactory : IFactory<IServer?, NodeBuildOptions>
{
    public IServer? Configure(NodeBuildOptions options, IServer? service)
    {
        return service;
    }

    public IServer? Create(NodeBuildOptions options)
    {
        if (options.Network == null)
        {
            return null;
        }
        
        if (options.Protocol == "mqtt")
        {
            return new MqttBroker
            (options.Index, options.Name, options.Address ?? options.Name, 
                options.Protocol, options.Network, options.NetworkGraph);
        }

        return new Server(options.Index, options.Name, options.Address ?? options.Name, 
            options.Protocol, options.Network, options.NetworkGraph)
        {
            Group = options.Group
        };
    }
}