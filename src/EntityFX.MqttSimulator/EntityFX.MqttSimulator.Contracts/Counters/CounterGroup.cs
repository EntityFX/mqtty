namespace EntityFX.MqttY.Contracts.Counters
{
    public class CounterGroup : ICounter
    {
        public CounterGroup(string name, string shortName, string groupType, string shortGroupType, bool enabled = true)
        {
            Name = name;
            GroupType = groupType;
            ShortGroupType = shortGroupType;
            ShortName = shortName;
            Enabled = enabled;
        }

        public virtual IEnumerable<ICounter> Counters { get; set; } = Enumerable.Empty<ICounter>();

        public string Name { get; init; }

        public string GroupType { get; init; }

        public string ShortGroupType { get; init; }

        public object Value => string.Empty;

        public string? UnitOfMeasure { get; init; }

        public object PreviousValue => string.Empty;
        public IEnumerable<KeyValuePair<long, object>> HistoryValues => Enumerable.Empty<KeyValuePair<long, object>>();

        // Tick stamps come from the shared simulator clock, so refreshing a
        // group never writes to the group instance itself.
        public long LastTicks => CounterClock.Ticks;

        public long LastSteps => CounterClock.Steps;

        public KeyValuePair<long, object> TickPreviousValue => new KeyValuePair<long, object>(0, 0);

        public KeyValuePair<long, object>? TickFirstValue { get; }
        public string ShortName { get; init; }
        public bool Enabled { get; set; }
        public bool HistoryEnabled { get; set; }
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

        public virtual void Refresh(long totalTicks, long totalSteps)
        {
            // Individual counters take their tick stamps from the shared
            // simulator clock (CounterClock), so neither this group nor the
            // counter tree needs per-tick writes. Overriders may still do their
            // own bookkeeping (e.g. MqttCounters RPS recalculation).
        }

        public override string ToString()
        {
            return $"{Name}:\n" 
                + string.Join("\n", Counters.Select(c => $"    {c.ToString()}"));
        }
    }
}
