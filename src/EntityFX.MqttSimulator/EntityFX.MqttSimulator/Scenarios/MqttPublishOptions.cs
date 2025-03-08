namespace EntityFX.MqttY.Scenarios
{
    public class MqttPublishOptions
    {
        public string Topic { get; init; } = string.Empty;

        public string ClientName { get; init; } = string.Empty;

        public byte[] Payload { get; init; } = Array.Empty<byte>();
    }
}
