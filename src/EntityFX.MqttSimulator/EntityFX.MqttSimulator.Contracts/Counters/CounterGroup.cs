namespace EntityFX.MqttY.Contracts.Counters
{
    public class CounterGroup : ICounter
    {
        public CounterGroup(string name)
        {
            Name = name;
        }

        public virtual ICounter[] Counters { get; init; } = Array.Empty<ICounter>();

        public string Name { get; init; } = string.Empty;

        public long Value => 0;

        public string? UnitOfMeasure { get; init; }

        public virtual void Refresh(long totalTicks)
        {
            foreach (var counter in Counters)
            {
                counter.Refresh(totalTicks);
            }
        }

        public override string ToString()
        {
            return $"{Name}:\n" 
                + string.Join("\n", Counters.Select(c => $"    {c.ToString()}"));
        }
    }
}
