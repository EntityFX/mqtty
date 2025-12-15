namespace EntityFX.MqttY.Contracts.Mqtt
{
    public record MqttSubscribtion(string TopicFilter, MqttQos MaximumQualityOfService);
}
