namespace EntityFX.MqttY.Federation;

public static class FederationStatistics
{
    public static SortedDictionary<long, long> Merge(IEnumerable<IReadOnlyDictionary<long, long>> histograms)
    {
        var result = new SortedDictionary<long, long>();
        foreach (var h in histograms) foreach (var kv in h) result[kv.Key] = result.GetValueOrDefault(kv.Key) + kv.Value;
        return result;
    }
    public static double? Quantile(IReadOnlyDictionary<long, long> histogram, double q, double tickMilliseconds)
    {
        var count = histogram.Values.Sum(); if (count == 0) return null;
        var rank = (long)Math.Ceiling(count * q); long cumulative = 0;
        foreach (var kv in histogram.OrderBy(k => k.Key)) { cumulative += kv.Value; if (cumulative >= rank) return kv.Key * tickMilliseconds; }
        throw new InvalidDataException("Invalid histogram.");
    }
}
