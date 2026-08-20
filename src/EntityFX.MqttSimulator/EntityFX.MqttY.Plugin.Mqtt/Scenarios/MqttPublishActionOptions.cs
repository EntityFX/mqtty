using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Plugin.Mqtt.Scenarios
{
    public class MqttPublishActionOptions
    {
        public string Topic { get; init; } = string.Empty;

        public string ClientName { get; init; } = string.Empty;

        public bool Multi { get; set; }

        public byte[] Payload { get; init; } = Array.Empty<byte>();

        /// <summary>Уровень QoS: AtMostOnce | AtLeastOnce | ExactlyOnce.</summary>
        public MqttQos Qos { get; init; } = MqttQos.AtLeastOnce;
    }
}
