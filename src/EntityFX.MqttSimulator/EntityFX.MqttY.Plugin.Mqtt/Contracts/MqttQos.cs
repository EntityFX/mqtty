namespace EntityFX.MqttY.Plugin.Mqtt.Contracts
{
    public enum MqttQos : byte
    {
        AtMostOnce = 0,
        AtLeastOnce = 1,
        ExactlyOnce = 2,
    }
}
