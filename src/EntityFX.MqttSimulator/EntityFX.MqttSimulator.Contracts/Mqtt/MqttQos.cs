namespace EntityFX.MqttY.Contracts.Mqtt
{
    public enum MqttQos : byte
    {
        AtMostOnce = 0,
        AtLeastOnce = 1,
        ExactlyOnce = 2,
    }
}
