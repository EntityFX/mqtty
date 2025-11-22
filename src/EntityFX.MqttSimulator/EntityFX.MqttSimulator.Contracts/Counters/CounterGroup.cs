namespace EntityFX.MqttY.Contracts.Counters
{
    public class CounterGroup : ICounter
    {
        public CounterGroup(string name, string groupType)
        {
            Name = name;
            GroupType = groupType;
        }

        public virtual IEnumerable<ICounter> Counters { get; set; } = Enumerable.Empty<ICounter>();

        public string Name { get; init; }

        public string GroupType { get; init; }

        public object Value => string.Empty;

        public string? UnitOfMeasure { get; init; }

        public object PreviousValue => string.Empty;
        public IEnumerable<KeyValuePair<long, object>> HistoryValues => Enumerable.Empty<KeyValuePair<long, object>>();

        public long LastTicks { get; private set; }

        public KeyValuePair<long, object> TickPreviousValue => new KeyValuePair<long, object>(0, 0);

        public KeyValuePair<long, object>? TickFirstValue { get; }

        KeyValuePair<long, object>? ICounter.TickPreviousValue { get; }

        public double Average()
        {
            return 0;
        }

        public void Clear()
        {
            foreach (var counter in Counters)
            {
                counter.Clear();
            }
        }

        public virtual void Refresh(long totalTicks)
        {
            foreach (var counter in Counters)
            {
                counter.Refresh(totalTicks);
            }
            LastTicks = totalTicks;
        }

        public override string ToString()
        {
            return $"{Name}:\n" 
                + string.Join("\n", Counters.Select(c => $"    {c.ToString()}"));
        }
    }
}
