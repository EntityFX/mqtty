using EntityFX.MqttY.Collections;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Helper;

namespace EntityFX.MqttY.Counter
{
    public class GenericCounter : INodeCounter<long>
    {
        public string Name { get; init; }

        public string ShortName { get; init; }

        public long Value => _value;

        public string? UnitOfMeasure { get; init; }

        object ICounter.Value => Value;

        public long PreviousValue => _privateValue;

        IEnumerable<KeyValuePair<long, object>> ICounter.HistoryValues => 
            _valueHistory.Select(vh => new KeyValuePair<long, object>(vh.Key, vh.Value));

        object ICounter.PreviousValue => PreviousValue;

        public long LastTicks { get; private set; }

        public long LastSteps { get; private set; }

        public bool Enabled { get; set; }

        public bool HistoryEnabled { get; set; }

        public IEnumerable<KeyValuePair<long, long>> HistoryValues => 
            _valueHistory;

        public KeyValuePair<long, long>? TickPreviousValue => _tickPreviousValue;

        KeyValuePair<long, object>? ICounter.TickPreviousValue => _tickPreviousValue != null ? new KeyValuePair<long, object>(_tickPreviousValue.Value.Key, _tickPreviousValue.Value.Value) : null;

        public KeyValuePair<long, long>? TickFirstValue => _tickFirstValue;


        KeyValuePair<long, object>? ICounter.TickFirstValue => _tickFirstValue != null ? new KeyValuePair<long, object>(_tickFirstValue.Value.Key, _tickFirstValue.Value.Value) : null;


        private readonly FixedSizedQueue<KeyValuePair<long, long>> _valueHistory;

        private long _value = 0;
        private long _privateValue = 0;
        private KeyValuePair<long, long>? _tickPreviousValue;

        private KeyValuePair<long, long>? _tickFirstValue;
        

        private readonly NormalizeUnits? _normalizeUnits;

        public GenericCounter(string name, string shortName,
            int historyDepth, string? unitOfMeasure = null, NormalizeUnits? normalizeUnits = null, bool enabled = true, bool historyEnabled = false)
        {
            Name = name;
            ShortName = shortName;
            UnitOfMeasure = unitOfMeasure;
            this._normalizeUnits = normalizeUnits;
            _valueHistory = new(historyDepth);
            HistoryEnabled = historyEnabled;
            Enabled = enabled;
        }

        public void Increment()
        {
            if (!Enabled)
            {
                return;
            }
            _privateValue = Value;
            Interlocked.Increment(ref _value);
            _tickPreviousValue = new KeyValuePair<long, long>(LastTicks, _privateValue);

            if (_tickFirstValue == null)
            {
                _tickFirstValue = new KeyValuePair<long, long>(LastTicks, _privateValue);
            }

            if (HistoryEnabled)
            {
                _valueHistory.Enqueue(_tickPreviousValue.Value);
            }
        }

        public void Add(long value)
        {
            _privateValue = Value;
            Interlocked.Add(ref _value, value);

            if (_tickFirstValue == null)
            {
                _tickFirstValue = new KeyValuePair<long, long>(LastTicks, _privateValue);
            }


            if (HistoryEnabled)
            {
                _valueHistory.Enqueue(new KeyValuePair<long, long>(LastTicks, _privateValue));
            }
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

        public void Refresh(long totalTicks, long totalSteps)
        {
            if (!Enabled)
            {
                return;
            }
            LastTicks = totalTicks;
            LastSteps = totalSteps;
        }

        public double Average()
        {
            if (_valueHistory.Count == 0) return 0;

            return _valueHistory.ToArray().Average(v => v.Value);
        }

        public void Clear()
        {
            _value = 0;
            _privateValue = 0;
            _tickFirstValue = null;
            _tickPreviousValue = null;
            _valueHistory.Clear();
        }
    }
}
