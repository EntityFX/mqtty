namespace EntityFX.MqttY.Plugin.Mqtt.Scenarios
{
    public class MqttPublishOptions
    {
        public MqttPublishActionOptions[] Actions { get; set; } = Array.Empty<MqttPublishActionOptions>();

        public byte[] Payload { get; init; } = Array.Empty<byte>();

        public TimeSpan PublishPeriod { get; init; }

        public  int PublishTicks { get; set; }
    }
}
