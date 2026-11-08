using EntityFX.MqttY.Experiment;

namespace EntityFX.MqttY.Federation;

public static class FederationStatistics
{
    public static SortedDictionary<long, long> Merge(IEnumerable<IReadOnlyDictionary<long, long>> histograms) =>
        HistogramStatistics.Merge(histograms);

    public static double? Quantile(IReadOnlyDictionary<long, long> histogram, double q, double tickMilliseconds) =>
        HistogramStatistics.Quantile(histogram, q, tickMilliseconds);
}
