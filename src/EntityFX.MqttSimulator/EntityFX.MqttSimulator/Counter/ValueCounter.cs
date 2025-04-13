using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Helper;

namespace EntityFX.MqttY.Counter
{
    internal class ValueCounter<TValue> : ICounter<TValue>, IWriteableCounter<TValue>
        where TValue : struct, IEquatable<TValue>
    {

        public string Name { get; init; }

        public TValue Value => _value;

        public string? UnitOfMeasure { get; init; }

        object ICounter.Value => Value;

        public TValue PreviousValue => _previousValue;

        object ICounter.PreviousValue => PreviousValue;

        public long LastTicks { get; private set; }

        private TValue _value = default;

        private TValue _previousValue = default;


        private readonly NormalizeUnits? normalizeUnits;

        public ValueCounter(string name, string? unitOfMeasure = null, NormalizeUnits? normalizeUnits = null)
        {
            Name = name;
            UnitOfMeasure = unitOfMeasure;
            this.normalizeUnits = normalizeUnits;
        }

        public void Refresh(long totalTicks)
        {
            LastTicks = totalTicks;
        }

        public void Set(TValue Value)
        {
            _previousValue = Value;
            _value = Value;
        }

        public override string ToString()
        {
            if (!Value.IsNumericType())
            {
                return Value.ToString() ?? string.Empty;
            }

            var doubleValue = Convert.ToDouble(Value);
            var stringValue = normalizeUnits switch {
                NormalizeUnits.Bit => doubleValue.ToHumanBits(UnitOfMeasure),
                NormalizeUnits.Byte => doubleValue.ToHumanBytes(UnitOfMeasure),
                NormalizeUnits.BiBit => doubleValue.ToHumanBiBits(UnitOfMeasure),
                NormalizeUnits.BiByte => doubleValue.ToHumanBiBytes(UnitOfMeasure),
                _ => $"{doubleValue}{(string.IsNullOrEmpty(UnitOfMeasure) ? string.Empty : $" {UnitOfMeasure}")}"
            };
            return $"{Name}={stringValue}";
        }
    }
}
