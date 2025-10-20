using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Plugin.Mqtt.Factories;

public class MqttClientFactory : IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>
{
    public IClient? Configure(NodeBuildOptions<NetworkBuildOption> options, IClient? service)
    {
        if (string.IsNullOrEmpty(options.ConnectsTo))
        {
            return service;
        }

        var mqttClient = service as IMqttClient;

        if (mqttClient == null)
        {
            return null;
        }

        var connectResult = mqttClient.Connect(options.ConnectsTo, false);

        var subscribes = options.Additional?.Additional?.GetValueOrDefault("subscribe");
        var subscribeQos = options.Additional?.Additional?.GetValueOrDefault("subscribeQos");

        if (subscribes?.Any() == true)
        {
            var qos = subscribeQos?.FirstOrDefault() ?? "AtLeastOnce";
            var qosEnum = Enum.Parse<MqttQos>(qos);

            foreach (var subscribeTopic in subscribes)
            {
                mqttClient.Subscribe(subscribeTopic, qosEnum);
            }
        }

        return service;
    }

    public IClient? Create(NodeBuildOptions<NetworkBuildOption> options)
    {
        if (options.Network == null)
        {
            return null;
        }


        var mqttPacketManager = options.ServiceProvider.GetRequiredService<IMqttPacketManager>();
        var mqttClient = new MqttClient(mqttPacketManager, options.Network, options.NetworkGraph, options.Index,
            options.Name, options.Address ?? options.Name,
            options.Protocol, options.Specification, options.Name,
            options.Additional!.TicksOptions!, options.Additional!.NetworkTypeOption!)
        {
            Group = options.Group,
            GroupAmount = options.GroupAmount
        };

        return mqttClient;
    }
}