using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Mqtt;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Utils;

internal class ServerFactory : IFactory<IServer?, Dictionary<string, string[]>>
{
    private readonly IServiceProvider serviceProvider;

    public ServerFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

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

        var mqttTopicEvaluator = serviceProvider.GetRequiredService<IMqttTopicEvaluator>();

        if (options.Protocol == "mqtt")
        {
            return new MqttBroker
            (options.Index, options.Name, options.Address ?? options.Name, 
                options.Protocol, options.Specification, options.Network, options.NetworkGraph, mqttTopicEvaluator);
        }

        return new Server(options.Index, options.Name, options.Address ?? options.Name, 
            options.Protocol, options.Specification, options.Network, options.NetworkGraph)
        {
            Group = options.Group
        };
    }
}