using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Plugin.Mqtt.Contracts;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Formatters;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Plugin.Mqtt.Factories;

public class MqttServerFactory : IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>
{
    private readonly IServiceProvider _serviceProvider;

    public MqttServerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IServer? Configure(NodeBuildOptions<NetworkBuildOption> options, IServer? service)
    {
        service?.Start();
        return service;
    }

    public IServer? Create(NodeBuildOptions<NetworkBuildOption> options)
    {
        if (options.Network == null)
        {
            return null;
        }

        var mqttTopicEvaluator = _serviceProvider.GetRequiredService<IMqttTopicEvaluator>();
        
            var mqttPacketManager = options.ServiceProvider.GetRequiredService<IMqttPacketManager>();
            return new MqttBroker
            (mqttPacketManager, options.Network, options.NetworkGraph, mqttTopicEvaluator, 
                options.Index, options.Name, options.Address ?? options.Name,
                options.Protocol, options.Specification, 
                options.Additional!.TicksOptions!, options.Additional!.NetworkTypeOption!);
    }
}