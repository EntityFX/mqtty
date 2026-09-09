namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    public sealed record MqttMessageSizeProfile(
        int MessageBytes,
        IDictionary<MqttQos, CalibratedMqttQosProfile> Qos);
}
