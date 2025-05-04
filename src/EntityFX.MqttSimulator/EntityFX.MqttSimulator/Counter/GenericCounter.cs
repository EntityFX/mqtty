using EntityFX.MqttY.Collections;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Helper;

namespace EntityFX.MqttY.Counter
{
    public class GenericCounter : INodeCounter<long>
    {
        public string Name { get; init; }

        public long Value => _value;

        public string? UnitOfMeasure { get; init; }

        object ICounter.Value => Value;

        public long PreviousValue => _privateValue;

        IEnumerable<KeyValuePair<long, object>> ICounter.HistoryValues => 
            _valueHistory.Select(vh => new KeyValuePair<long, object>(vh.Key, vh.Value));

        object ICounter.PreviousValue => PreviousValue;

        public long LastTicks { get; private set; }

        public IEnumerable<KeyValuePair<long, long>> HistoryValues => throw new NotImplementedException();

        private readonly FixedSizedQueue<KeyValuePair<long, long>> _valueHistory = new(1000);

        private long _value = 0;
        private long _privateValue = 0;

        private readonly NormalizeUnits? _normalizeUnits;

        public GenericCounter(string name, string? unitOfMeasure = null, NormalizeUnits? normalizeUnits = null)
        {
            Name = name;
            UnitOfMeasure = unitOfMeasure;
            this._normalizeUnits = normalizeUnits;
        }

        public void Increment()
        {
            Interlocked.Increment(ref _value);
        }

        public void Add(long value)
        {
            _privateValue = Value;
            Interlocked.Add(ref _value, value);
            _valueHistory.Enqueue(new KeyValuePair<long, long>(LastTicks, value));
        }

        public override string ToString()
        {
            var doubleValue = Convert.ToDouble(Value);
            var stringValue = _normalizeUnits switch
            {
                NormalizeUnits.Bit => doubleValue.ToHumanBits(),
                NormalizeUnits.Byte => doubleValue.ToHumanBytes(),
                NormalizeUnits.BiBit => doubleValue.ToHumanBiBits(),
                NormalizeUnits.BiByte => doubleValue.ToHumanBiBytes(),
                _ => $"{doubleValue:f2}{(string.IsNullOrEmpty(UnitOfMeasure) ? string.Empty : $" {UnitOfMeasure}")}"
            };
            return $"{Name}={stringValue}";
        }

        public void Refresh(long totalTicks)
        {
            LastTicks = totalTicks;
        }
    }
}
