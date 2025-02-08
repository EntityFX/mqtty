namespace EntityFX.MqttY.Contracts.Options
{
    public class NodeOption
    {
        public NodeOptionType Type { get; set; }
        public string? Specification { get; set; }

        public string? Network { get; set; }

        public string? ConnectsToServer { get; set; }
    }

    public enum NodeOptionType
    {
        Client, Server
    }
}
