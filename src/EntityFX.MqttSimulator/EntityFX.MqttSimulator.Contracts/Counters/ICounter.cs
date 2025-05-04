namespace EntityFX.MqttY.Contracts.Counters
{
    public interface ICounter<TValue> : ICounter
        where TValue: struct, IEquatable<TValue>
    {
        new TValue Value { get; }

        new TValue PreviousValue { get; }

        new IEnumerable<KeyValuePair<long, TValue>> HistoryValues { get; }
    }

    public interface ICounter
    {
        string Name { get; init; }

        string? UnitOfMeasure { get; init; }

        object Value { get; }

        object PreviousValue { get; }
        
        IEnumerable<KeyValuePair<long, object>> HistoryValues { get; }

        long LastTicks { get; }

        void Refresh(long totalTicks);
    }
}
