namespace EntityFX.MqttY.Contracts.Options
{
    public class NetworkGraphOption
    {
        public SortedDictionary<string, NetworkNodeOption> Networks { get; set; } = new();
        public SortedDictionary<string, NetworkOptions> NetworkTypes { get; set; } = new();

        public SortedDictionary<string, NodeOption> Nodes { get; set; } = new();

        public TicksOptions Ticks { get; set; } = new();

        public bool EnableCounters { get; set; }

    }
}
