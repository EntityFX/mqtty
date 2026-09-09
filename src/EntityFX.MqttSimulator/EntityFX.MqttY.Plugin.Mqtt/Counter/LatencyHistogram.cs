using System.Collections.ObjectModel;

namespace EntityFX.MqttY.Plugin.Mqtt.Counter
{
    internal sealed class LatencyHistogram
    {
        private readonly SortedDictionary<long, long> _counts = new();
        private long _total;

        public void Record(long elapsedTicks)
        {
            if (elapsedTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
            _counts[elapsedTicks] = _counts.GetValueOrDefault(elapsedTicks) + 1;
            _total++;
        }

        public long? NearestRank(double quantile)
        {
            if (quantile <= 0 || quantile > 1) throw new ArgumentOutOfRangeException(nameof(quantile));
            if (_total == 0) return null;

            var rank = Math.Clamp((long)Math.Ceiling(quantile * _total), 1, _total);
            long cumulative = 0;
            foreach (var (ticks, count) in _counts)
            {
                cumulative += count;
                if (cumulative >= rank) return ticks;
            }

            return _counts.Last().Key;
        }

        public IReadOnlyDictionary<long, long> Snapshot() =>
            new ReadOnlyDictionary<long, long>(new SortedDictionary<long, long>(_counts));
    }
}
