namespace EntityFX.MqttY.Plugin.Mqtt.Counter
{
    internal interface IMqttClientPublishStateSink
    {
        void FailPublish(ushort packetId);
    }
}
