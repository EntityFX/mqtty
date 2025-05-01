namespace EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt
{

    public class MqttRelayConfiguration
    {
        public Dictionary<string, MqttListenConfigurationItem> ListenTopics { get; set; } = new();

        public Dictionary<string, MqttRelayConfigurationItem> RelayTopics { get; set; } = new();

        public Dictionary<string, string[]> RouteMap { get; set; } = new();

        public class MqttListenConfigurationItem
        {
            public string Server { get; set; } = string.Empty;

            public string[] Topics { get; set; } = new string[0];
        }

        public class MqttRelayConfigurationItem
        {
            public string Server { get; set; } = string.Empty;
            public bool ReplaceRelaySegment  { get; set; } = false;

            public string TopicPrefix { get; set; } = string.Empty;
        }
    }
}
