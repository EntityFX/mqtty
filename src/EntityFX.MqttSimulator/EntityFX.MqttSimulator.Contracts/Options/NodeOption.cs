namespace EntityFX.MqttY.Contracts.Options
{
    public class NodeOption
    {
        public NodeOptionType Type { get; set; }
        public string? Specification { get; set; }

        public string? Links { get; set; }

        public string? Connects { get; set; }
    }

    public enum NodeOptionType
    {
        Client, Server
    }
}
