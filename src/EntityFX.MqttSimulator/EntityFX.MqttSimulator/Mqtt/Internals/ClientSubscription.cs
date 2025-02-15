using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Mqtt.Internals
{
    internal class ClientSubscription
    {
        public string ClientId { get; set; } = string.Empty;

        public string TopicFilter { get; set; } = string.Empty;

        public MqttQos MaximumQualityOfService { get; set; }
    }
}
