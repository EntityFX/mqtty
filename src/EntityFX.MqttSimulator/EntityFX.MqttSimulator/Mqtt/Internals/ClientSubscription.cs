using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Mqtt.Internals
{
    internal class ClientSubscription
    {
        public string ClientId { get; set; }

        public string TopicFilter { get; set; }

        public MqttQos MaximumQualityOfService { get; set; }
    }
}
