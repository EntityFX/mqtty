using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Mqtt;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Factories;

internal class ServerFactory : IFactory<IServer?, NodeBuildOptions<Dictionary<string, string[]>>>
{
    private readonly IServiceProvider _serviceProvider;

    public ServerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
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

        var mqttTopicEvaluator = _serviceProvider.GetRequiredService<IMqttTopicEvaluator>();

        if (options.Protocol == "mqtt")
        {
            var mqttPacketManager = options.ServiceProvider.GetRequiredService<IMqttPacketManager>();
            return new MqttBroker
            (mqttPacketManager, options.Network, options.NetworkGraph, mqttTopicEvaluator, options.Index, options.Name, options.Address ?? options.Name,
                options.Protocol, options.Specification);
        }

        return new Server(options.Index, options.Name, options.Address ?? options.Name,
            options.Protocol, options.Specification, options.Network, options.NetworkGraph)
        {
            Group = options.Group
        };
    }
}