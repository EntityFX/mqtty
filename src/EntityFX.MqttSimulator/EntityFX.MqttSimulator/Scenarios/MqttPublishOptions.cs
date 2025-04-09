namespace EntityFX.MqttY.Scenarios
{
    public class MqttPublishOptions
    {
        public MqttPublishActionOptions[] Actions { get; set; } = Array.Empty<MqttPublishActionOptions>();

        public byte[] Payload { get; init; } = Array.Empty<byte>();
    }
}
