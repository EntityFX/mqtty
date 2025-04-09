namespace EntityFX.MqttY.Scenarios
{
    public class MqttPublishActionOptions
    {
        public string Topic { get; init; } = string.Empty;

        public string ClientName { get; init; } = string.Empty;

        public bool Multi { get; set; }

        public byte[] Payload { get; init; } = Array.Empty<byte>();
    }
}
