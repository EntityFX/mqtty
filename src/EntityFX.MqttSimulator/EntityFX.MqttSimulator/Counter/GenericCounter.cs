using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Helper;

namespace EntityFX.MqttY.Counter
{
    internal class GenericCounter : INodeCounter<long>
    {
        public string Name { get; init; }

        public long Value => _value;

        public string? UnitOfMeasure { get; init; }

        object ICounter.Value => Value;

        public long PreviousValue => _privateValue;

        object ICounter.PreviousValue => PreviousValue;

        public long LastTicks { get; private set; }

        private long _value = 0;
        private long _privateValue = 0;

        private readonly NormalizeUnits? normalizeUnits;

        public GenericCounter(string name, string? unitOfMeasure = null, NormalizeUnits? normalizeUnits = null)
        {
            Name = name;
            UnitOfMeasure = unitOfMeasure;
            this.normalizeUnits = normalizeUnits;
        }

        public void Increment()
        {
            Interlocked.Increment(ref _value);
        }

        public void Add(long value)
        {
            Interlocked.Add(ref _value, value);
        }

        public override string ToString()
        {
            var doubleValue = Convert.ToDouble(Value);
            var stringValue = normalizeUnits switch
            {
                NormalizeUnits.Bit => doubleValue.ToHumanBits(),
                NormalizeUnits.Byte => doubleValue.ToHumanBytes(),
                NormalizeUnits.BiBit => doubleValue.ToHumanBiBits(),
                NormalizeUnits.BiByte => doubleValue.ToHumanBiBytes(),
                _ => $"{doubleValue}{(string.IsNullOrEmpty(UnitOfMeasure) ? string.Empty : $" {UnitOfMeasure}")}"
            };
            return $"{Name}={stringValue}";
        }

        public void Refresh(long totalTicks)
        {
            LastTicks = totalTicks;
        }
    }
}
