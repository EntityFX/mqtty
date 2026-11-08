namespace EntityFX.MqttY.Experiment;

/// <summary>Tick-latency histogram operations shared by LB and Federation.</summary>
public static class HistogramStatistics
{
    public static SortedDictionary<long, long> Merge(IEnumerable<IReadOnlyDictionary<long, long>> histograms)
    {
        var merged = new SortedDictionary<long, long>();
        foreach (var histogram in histograms)
        {
            foreach (var (tick, count) in histogram)
            {
                merged[tick] = merged.GetValueOrDefault(tick) + count;
            }
        }
        return merged;
    }

    public static double? Quantile(IReadOnlyDictionary<long, long> histogram, double q, double tickMilliseconds)
    {
        var total = histogram.Values.Sum();
        if (total == 0)
        {
            return null;
        }

        var rank = Math.Max(1, (long)Math.Ceiling(q * total));
        long cumulative = 0;
        foreach (var (tick, count) in histogram.OrderBy(pair => pair.Key))
        {
            if ((cumulative += count) >= rank)
            {
                return tick * tickMilliseconds;
            }
        }

        throw new InvalidDataException("Invalid histogram.");
    }
}
