using EntityFX.MqttY.Collections;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Helper;

namespace EntityFX.MqttY.Counter
{
    public class ValueCounter<TValue> : ICounter<TValue>, IWriteableCounter<TValue>
        where TValue : struct, IEquatable<TValue>
    {

        public string Name { get; init; }

        public TValue Value => _value;

        public string? UnitOfMeasure { get; init; }

        object ICounter.Value => Value;

        public TValue PreviousValue => _previousValue;

        IEnumerable<KeyValuePair<long, object>> ICounter.HistoryValues => 
            _valueHistory.Select(vh => new KeyValuePair<long, object>(vh.Key, vh.Value));

        object ICounter.PreviousValue => PreviousValue;

        public long LastTicks { get; private set; }

        public IEnumerable<KeyValuePair<long, TValue>> HistoryValues => _valueHistory;

        public KeyValuePair<long, TValue>? TickPreviousValue => _tickPreviousValue;

        KeyValuePair<long, object>? ICounter.TickPreviousValue => _tickPreviousValue != null 
            ? new KeyValuePair<long, object>(_tickPreviousValue.Value.Key, _tickPreviousValue.Value.Value)
            : null;

        public KeyValuePair<long, TValue>? TickFirstValue => _tickFirstValue;

        KeyValuePair<long, object>? ICounter.TickFirstValue => _tickFirstValue != null
            ? new KeyValuePair<long, object>(_tickFirstValue.Value.Key, _tickFirstValue.Value.Value)
            : null;

        private TValue _value = default;

        private TValue _previousValue = default;

        private KeyValuePair<long, TValue>? _tickFirstValue;

        private KeyValuePair<long, TValue>? _tickPreviousValue;

        private readonly FixedSizedQueue<KeyValuePair<long, TValue>> _valueHistory;


        private readonly NormalizeUnits? _normalizeUnits;

        public ValueCounter(string name, int historyDepth,  
            string? unitOfMeasure = null, NormalizeUnits? normalizeUnits = null)
        {
            Name = name;
            UnitOfMeasure = unitOfMeasure;
            this._normalizeUnits = normalizeUnits;
            _valueHistory = new(historyDepth);
        }

        public void Refresh(long totalTicks)
        {
            LastTicks = totalTicks;
        }

        public void Set(TValue value)
        {
            if (_tickFirstValue == null)
            {
                _tickFirstValue = new KeyValuePair<long, TValue>(LastTicks, value);
            }

            _previousValue = value;
            _value = value;
            _valueHistory.Enqueue(new KeyValuePair<long, TValue>(LastTicks, value));
        }

        public double Average()
        {
            if (_valueHistory.Count == 0) return 0;

            if (Value is not long
                && Value is not int
                && Value is not short
                && Value is not double
                && Value is not float
                && Value is not decimal)
            {
                return 0;
            }

            var avg = _valueHistory.Average(v => Convert.ToDouble(v.Value));

            return avg;
        }

        public void Clear()
        {
            _value = default(TValue);
            _previousValue = default(TValue);
            _tickFirstValue = null;
            _tickPreviousValue = null;
            _valueHistory.Clear(); 
        }

        public override string ToString()
        {
            if (!Value.IsNumericType())
            {
                return $"{Name}={(Value.ToString() ?? string.Empty)}";
            }

            var doubleValue = Convert.ToDouble(Value);
            var stringValue = _normalizeUnits switch {
                NormalizeUnits.Bit => doubleValue.ToHumanBits(UnitOfMeasure),
                NormalizeUnits.Byte => doubleValue.ToHumanBytes(UnitOfMeasure),
                NormalizeUnits.BiBit => doubleValue.ToHumanBiBits(UnitOfMeasure),
                NormalizeUnits.BiByte => doubleValue.ToHumanBiBytes(UnitOfMeasure),
                _ => $"{doubleValue:f2}{(string.IsNullOrEmpty(UnitOfMeasure) ? string.Empty : $" {UnitOfMeasure}")}"
            };
            return $"{Name}={stringValue}";
        }
    }
}
