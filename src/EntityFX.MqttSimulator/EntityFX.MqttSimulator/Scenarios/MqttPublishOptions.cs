namespace EntityFX.MqttY.Scenarios
{
    public class MqttPublishOptions
    {
        public string Topic { get; init; } = string.Empty;

        public string MqttClientName { get; init; } = string.Empty;

        public object? Payload { get; init; } = null;
    }
}
