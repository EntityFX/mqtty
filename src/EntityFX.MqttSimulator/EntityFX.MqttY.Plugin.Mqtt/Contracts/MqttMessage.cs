namespace EntityFX.MqttY.Plugin.Mqtt.Contracts
{
    public record MqttMessage(string Topic, byte[] Payload, MqttQos Qos, String Broker);
}
