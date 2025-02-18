using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Mqtt;

namespace EntityFX.MqttY.Utils;

internal class ClientFactory : IFactory<IClient?, NodeBuildOptions>
{
    public IClient? Configure(NodeBuildOptions options, IClient? service)
    {
        if (string.IsNullOrEmpty(options.ConnectsTo))
        {
            return service;
        }

        if (service?.ProtocolType == "mqtt" && service is IMqttClient mqttClient)
        {
            var connectResult = mqttClient.ConnectAsync(options.ConnectsTo, false).Result;

            var subscribes = options.Additional?.GetValueOrDefault("subscribe");
            var subscribeQos =  options.Additional?.GetValueOrDefault("subscribeQos");

            if (subscribes?.Any() == true)
            {
                var qos = subscribeQos?.FirstOrDefault() ?? "AtLeastOnce";
                var qosEnum = Enum.Parse<MqttQos>(qos);

                foreach (var subscribeTopic in subscribes)
                {
                    mqttClient.SubscribeAsync(subscribeTopic, qosEnum).Wait();
                }


            }

        }  else
        {
            service?.Connect(options.ConnectsTo);
        }

        return service;
    }

    public IClient? Create(NodeBuildOptions options)
    {
        if (options.Network == null)
        {
            return null;
        }
        
        if (options.Protocol == "mqtt")
        {
            var mqttClient = new MqttClient(options.Index,
                options.Name, options.Address ?? options.Name, 
                options.Protocol, options.Network, options.NetworkGraph, options.Name);

            return mqttClient;
        }

        return new Client(options.Index,options.Name, options.Address ?? options.Name, options.Protocol, 
            options.Network, options.NetworkGraph)
        {
            Group = options.Group
        };
    }
}