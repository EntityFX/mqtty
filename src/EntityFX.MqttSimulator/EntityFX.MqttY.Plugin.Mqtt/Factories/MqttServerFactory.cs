using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
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
        //            options.Network, options.NetworkGraph, 
        var mqttPacketManager = options.ServiceProvider.GetRequiredService<IMqttPacketManager>();
        var mqttBroker = new MqttBroker(mqttPacketManager,
            mqttTopicEvaluator,
            options.Index, options.Name, options.Address ?? options.Name,
            options.Protocol, options.Specification,
            options.Additional!.TicksOptions!);

        options.Network.AddServer(mqttBroker);
        options.NetworkGraph.AddServer(mqttBroker);

        return mqttBroker;
    }
}