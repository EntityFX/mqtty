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

        public long PreviousValue => _value;

        IEnumerable<KeyValuePair<long, object>> ICounter.HistoryValues => 
            _valueHistory.Select(vh => new KeyValuePair<long, object>(vh.Key, vh.Value));

        object ICounter.PreviousValue => PreviousValue;

        // Tick stamps come from the shared simulator clock; per-instance writes
        // on every refresh tick dominated counter CPU (see optimization report).
        public long LastTicks => CounterClock.Ticks;

        public long LastSteps => CounterClock.Steps;

        public bool Enabled { get; set; }

        public bool HistoryEnabled { get; set; }

        public IEnumerable<KeyValuePair<long, long>> HistoryValues => 
            _valueHistory;

        public KeyValuePair<long, long>? TickPreviousValue => null;

        KeyValuePair<long, object>? ICounter.TickPreviousValue => null;

        public KeyValuePair<long, long>? TickFirstValue => _tickFirstValue;


        KeyValuePair<long, object>? ICounter.TickFirstValue => _tickFirstValue != null ? new KeyValuePair<long, object>(_tickFirstValue.Value.Key, _tickFirstValue.Value.Value) : null;


        private readonly FixedSizedQueue<KeyValuePair<long, long>> _valueHistory;

        private long _value = 0;
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

            // Hot path: a single atomic update. Writing the previous/tick fields
            // on every increment made counters shared by many workers (the client
            // LAN inbound/outbound counters in particular) bounce cache lines.
            var value = Interlocked.Increment(ref _value);

            if (_tickFirstValue == null)
            {
                _tickFirstValue = new KeyValuePair<long, long>(LastTicks, value - 1);
            }

            if (HistoryEnabled)
            {
                _valueHistory.Enqueue(new KeyValuePair<long, long>(LastTicks, value - 1));
            }
        }

        public void Add(long value)
        {
            if (!Enabled || value == 0)
            {
                return;
            }

            var next = Interlocked.Add(ref _value, value);

            if (_tickFirstValue == null)
            {
                _tickFirstValue = new KeyValuePair<long, long>(LastTicks, next - value);
            }

            if (HistoryEnabled)
            {
                _valueHistory.Enqueue(new KeyValuePair<long, long>(LastTicks, next - value));
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
            // Tick stamps are published globally via CounterClock; nothing to
            // update per instance.
        }

        public double Average()
        {
            if (_valueHistory.Count == 0) return 0;

            return _valueHistory.ToArray().Average(v => v.Value);
        }

        public void Clear()
        {
            _value = 0;
            _tickFirstValue = null;
            _valueHistory.Clear();
        }
    }
}
