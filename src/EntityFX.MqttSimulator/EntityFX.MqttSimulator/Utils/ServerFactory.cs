using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Mqtt;

namespace EntityFX.MqttY.Utils;

internal class ServerFactory : IFactory<IServer?, Dictionary<string, string[]>>
{
    public IServer? Configure(NodeBuildOptions<Dictionary<string, string[]>> options, IServer? service)
    {
        service?.Start();
        return service;
    }

    public IServer? Create(NodeBuildOptions<Dictionary<string, string[]>> options)
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