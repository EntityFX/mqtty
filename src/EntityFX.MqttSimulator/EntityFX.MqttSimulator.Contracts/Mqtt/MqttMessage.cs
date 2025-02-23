namespace EntityFX.MqttY.Contracts.Mqtt
{
    public record MqttMessage(string Topic, byte[] Payload, MqttQos Qos, String Broker);
}
