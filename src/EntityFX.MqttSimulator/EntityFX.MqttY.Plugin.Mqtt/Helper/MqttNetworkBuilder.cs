using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Helper;

namespace EntityFX.MqttY.Plugin.Mqtt.Helper;

public class MqttNetworkBuilder : MqttNetworkBuilderBase
{
    private readonly IMqttPacketManager mqttPacketManager;
    private readonly IMqttTopicEvaluator mqttTopicEvaluator;

    public MqttNetworkBuilder(INetworkSimulator networkSimulator, IMqttPacketManager mqttPacketManager, IMqttTopicEvaluator mqttTopicEvaluator)
        : base(networkSimulator)
    {
        this.mqttPacketManager = mqttPacketManager;
        this.mqttTopicEvaluator = mqttTopicEvaluator;
    }

    protected override IServer CreateServer(TicksOptions ticksOptions, int ix, string fullName, string address)
    {
        return new MqttBroker(mqttPacketManager, mqttTopicEvaluator, ix, fullName, address, "mqtt", "mqtt", ticksOptions);
    }

    protected override IClient CreateClient(TicksOptions ticksOptions, int ix, string name, string fullName, string address)
    {
        return new MqttClient(mqttPacketManager, ix, fullName, address, "mqtt", "mqtt", name, ticksOptions);
    }
}
