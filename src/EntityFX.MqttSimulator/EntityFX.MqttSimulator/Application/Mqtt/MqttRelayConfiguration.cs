namespace EntityFX.MqttY.Application.Mqtt
{
    public class MqttRelayConfiguration
    {
        public Dictionary<string, MqttRelayConfigurationItem> ListenTopics { get; set; } = new();

        public Dictionary<string, MqttRelayConfigurationItem> RelayTopics { get; set; } = new();

        public Dictionary<string, string[]> RouteMap { get; set; } = new();

        public class MqttRelayConfigurationItem
        {
            public string Server { get; set; } = string.Empty;

            public string[] Topics { get; set; } = new string[0];
        }
    }
}
