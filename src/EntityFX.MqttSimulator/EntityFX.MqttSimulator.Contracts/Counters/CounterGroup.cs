namespace EntityFX.MqttY.Contracts.Counters
{
    public class CounterGroup : ICounter
    {
        public CounterGroup(string name)
        {
            Name = name;
        }

        public ICounter[] Counters { get; init; } = Array.Empty<ICounter>();

        public string Name { get; init; } = string.Empty;

        public long Value => 0;

        public override string ToString()
        {
            return $"{Name}:\n" 
                + string.Join("\n", Counters.Select(c => $"    {c.ToString()}"));
        }
    }
}
