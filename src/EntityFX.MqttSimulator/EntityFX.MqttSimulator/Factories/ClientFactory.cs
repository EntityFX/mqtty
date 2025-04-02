using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Mqtt;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Factories;

internal class ClientFactory : IFactory<IClient?, NodeBuildOptions<Dictionary<string, string[]>>>
{
    public IClient? Configure(NodeBuildOptions<Dictionary<string, string[]>> options, IClient? service)
    {
        if (string.IsNullOrEmpty(options.ConnectsTo))
        {
            return service;
        }

        if (service?.ProtocolType == "mqtt" && service is IMqttClient mqttClient)
        {
            var connectResult = mqttClient.ConnectAsync(options.ConnectsTo, false).Result;

            var subscribes = options.Additional?.GetValueOrDefault("subscribe");
            var subscribeQos = options.Additional?.GetValueOrDefault("subscribeQos");

            if (subscribes?.Any() == true)
            {
                var qos = subscribeQos?.FirstOrDefault() ?? "AtLeastOnce";
                var qosEnum = Enum.Parse<MqttQos>(qos);

                foreach (var subscribeTopic in subscribes)
                {
                    mqttClient.SubscribeAsync(subscribeTopic, qosEnum).Wait();
                }


            }

        }
        else
        {
            service?.ConnectAsync(options.ConnectsTo);
        }

        return service;
    }

    public IClient? Create(NodeBuildOptions<Dictionary<string, string[]>> options)
    {
        if (options.Network == null)
        {
            return null;
        }

        if (options.Protocol == "mqtt")
        {
            var mqttPacketManager = options.ServiceProvider.GetRequiredService<IMqttPacketManager>();
            var mqttClient = new MqttClient(mqttPacketManager, options.Network, options.NetworkGraph, options.Index,
                options.Name, options.Address ?? options.Name,
                options.Protocol, options.Specification, options.Name)
            {
                Group = options.Group,
                GroupAmount = options.GroupAmount
            };

            return mqttClient;
        }

        return new Client(options.Index, options.Name, options.Address ?? options.Name, options.Protocol,
            options.Specification,
            options.Network, options.NetworkGraph)
        {
            Group = options.Group
        };
    }
}