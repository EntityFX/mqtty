using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;
using Microsoft.Extensions.Options;
using System.Net;
using System.Xml.Linq;

namespace EntityFX.MqttY.Plugin.Mqtt.Helper;

public class MqttNetworkBuilder : NetworkBuilderBase
{
    private readonly IMqttPacketManager mqttPacketManager;
    private readonly IMqttTopicEvaluator mqttTopicEvaluator;
    private readonly IClientBuilder clientBuilder;

    public MqttNetworkBuilder(INetworkSimulator networkSimulator, IMqttPacketManager mqttPacketManager, IMqttTopicEvaluator mqttTopicEvaluator, IClientBuilder clientBuilder)
        : base(networkSimulator)
    {
        this.mqttPacketManager = mqttPacketManager;
        this.mqttTopicEvaluator = mqttTopicEvaluator;
        this.clientBuilder = clientBuilder;
    }

    protected override IServer CreateServer(TicksOptions ticksOptions, int ix, string fullName, string address)
    {
        return new MqttBroker(mqttPacketManager, mqttTopicEvaluator, ix, fullName, address, "mqtt", "mqtt", ticksOptions);
    }

    protected override IClient CreateClient(TicksOptions ticksOptions, int ix, string name, string fullName, string address)
    {
        return new MqttClient(mqttPacketManager, ix, fullName, address, "mqtt", "mqtt", name, ticksOptions);
    }

    protected override IApplication CreateApplication(
        TicksOptions ticksOptions, int ix, string name, string fullName, 
        string address, string specification,
        NetworkBuilderApplicationFunc<object>? appOptionsFunc)
    {
        switch (specification)
        {
            case "mqtt-relay":
                return new MqttRelay(ix, fullName, address,
                    "mqtt", specification, clientBuilder, mqttTopicEvaluator,
                    ticksOptions,
                    appOptionsFunc?.Invoke(ix, fullName, specification) as MqttRelayConfiguration);
            case "mqtt-receiver":
                return new MqttReceiver(clientBuilder, ix, name, address, 
                    "mqtt", specification, ticksOptions,
                    appOptionsFunc?.Invoke(ix, fullName, specification) as MqttReceiverConfiguration
                );
        }

        return new Application<string>(ix, fullName, address, "mqtt", specification, ticksOptions, null);
    }
}
