using System.Collections.ObjectModel;

namespace EntityFX.MqttY.Plugin.Mqtt.Counter
{
    /// <summary>
    /// Dense tick-indexed latency histogram. Recording is O(1) and does not
    /// allocate; quantiles scan the non-zero range once. Very large latencies
    /// spill into a sorted overflow map so a pathological sample cannot force a
    /// multi-megabyte array.
    /// </summary>
    internal sealed class LatencyHistogram
    {
        private const int MaxDenseTicks = 1 << 20;
        private long[] _counts = new long[64];
        private int _maxIndex = -1;
        private SortedDictionary<long, long>? _overflow;
        private long _total;

        public void Record(long elapsedTicks)
        {
            if (elapsedTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedTicks));

            _total++;
            if (elapsedTicks >= MaxDenseTicks)
            {
                _overflow ??= new SortedDictionary<long, long>();
                _overflow[elapsedTicks] = _overflow.GetValueOrDefault(elapsedTicks) + 1;
                return;
            }

            var index = (int)elapsedTicks;
            if (index >= _counts.Length)
            {
                Array.Resize(ref _counts, GrowTo(index + 1));
            }

            _counts[index]++;
            if (index > _maxIndex)
            {
                _maxIndex = index;
            }
        }

        public long? NearestRank(double quantile)
        {
            if (quantile <= 0 || quantile > 1) throw new ArgumentOutOfRangeException(nameof(quantile));
            if (_total == 0) return null;

            var rank = Math.Clamp((long)Math.Ceiling(quantile * _total), 1, _total);
            long cumulative = 0;

            for (var ticks = 0; ticks <= _maxIndex; ticks++)
            {
                cumulative += _counts[ticks];
                if (cumulative >= rank) return ticks;
            }

            if (_overflow != null)
            {
                foreach (var (ticks, count) in _overflow)
                {
                    cumulative += count;
                    if (cumulative >= rank) return ticks;
                }
            }

            return _maxIndex >= 0 ? _maxIndex : _overflow?.Keys.Last();
        }

        public IReadOnlyDictionary<long, long> Snapshot()
        {
            var snapshot = new SortedDictionary<long, long>();

            for (var ticks = 0; ticks <= _maxIndex; ticks++)
            {
                var count = _counts[ticks];
                if (count != 0)
                {
                    snapshot[ticks] = count;
                }
            }

            if (_overflow != null)
            {
                foreach (var (ticks, count) in _overflow)
                {
                    snapshot[ticks] = count;
                }
            }

            return new ReadOnlyDictionary<long, long>(snapshot);
        }

        private int GrowTo(int required)
        {
            var size = _counts.Length;
            while (size < required)
            {
                size <<= 1;
            }

            return size;
        }
    }
}
