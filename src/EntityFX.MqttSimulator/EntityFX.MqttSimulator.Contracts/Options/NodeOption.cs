namespace EntityFX.MqttY.Contracts.Options
{
    public class NodeOption
    {
        public NodeOptionType Type { get; set; }
        public string? Protocol { get; set; }

        public string? Specification { get; set; }

        public string? Network { get; set; }

        public string? ConnectsToServer { get; set; }

        public int? Quantity { get; set; }
        
        public int? Index { get; set; }

        public int SendTicks { get; set; }

        public Dictionary<string, string[]> Additional { get; init; } = new Dictionary<string, string[]>();

        /// <summary>Тип реального брокера: Mosquitto, Aedes, ActiveMQ, EMQX.</summary>
        public string? Broker { get; set; }

        public object? Configuration { get; set; }
    }
}
