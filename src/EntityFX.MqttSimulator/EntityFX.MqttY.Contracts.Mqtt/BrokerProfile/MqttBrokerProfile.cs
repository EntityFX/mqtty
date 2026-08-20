namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    /// <summary>
    /// Полный эталонный профиль одного MQTT-брокера для трёх уровней QoS.
    /// </summary>
    public sealed class MqttBrokerProfile
    {
        public string BrokerType { get; init; } = string.Empty;

        public MqttQosProfile Qos0 { get; init; } = new();

        public MqttQosProfile Qos1 { get; init; } = new();

        public MqttQosProfile Qos2 { get; init; } = new();

        public MqttQosProfile ForQos(MqttQos qos) => qos switch
        {
            MqttQos.AtMostOnce => Qos0,
            MqttQos.AtLeastOnce => Qos1,
            MqttQos.ExactlyOnce => Qos2,
            _ => Qos0
        };
    }
}