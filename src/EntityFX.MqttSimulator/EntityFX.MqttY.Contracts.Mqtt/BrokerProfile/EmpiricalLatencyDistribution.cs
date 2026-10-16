namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;

public sealed record LatencyBin(double Milliseconds, long Count);

/// <summary>A weighted empirical CDF. Rare maxima retain their observed frequency.</summary>
public sealed class EmpiricalLatencyDistribution
{
    private readonly LatencyBin[] bins;
    private readonly long[] cumulative;
    public IReadOnlyList<LatencyBin> Bins => Array.AsReadOnly(bins);
    public long Count { get; }

    public EmpiricalLatencyDistribution(IEnumerable<LatencyBin> source)
    {
        bins = source.ToArray();
        if (bins.Length == 0 || bins.Length > 1000000) throw new InvalidDataException("Invalid empirical latency bin count.");
        cumulative = new long[bins.Length];
        double previous = -1;
        long count = 0;
        for (var index = 0; index < bins.Length; index++)
        {
            var bin = bins[index];
            if (!double.IsFinite(bin.Milliseconds) || bin.Milliseconds < 0 || bin.Milliseconds <= previous || bin.Count <= 0)
                throw new InvalidDataException("Latency bins must be finite, nonnegative, strictly increasing with positive counts.");
            previous = bin.Milliseconds;
            try { count = checked(count + bin.Count); }
            catch (OverflowException error) { throw new InvalidDataException("Latency sample count overflow.", error); }
            cumulative[index] = count;
        }
        Count = count;
    }

    public double Sample(double uniform)
    {
        if (!double.IsFinite(uniform) || uniform < 0 || uniform > 1) throw new ArgumentOutOfRangeException(nameof(uniform));
        var rank = uniform == 1 ? Count : Math.Min(Count, (long)Math.Floor(uniform * Count) + 1);
        var index = Array.BinarySearch(cumulative, rank);
        if (index < 0) index = ~index;
        return bins[index].Milliseconds;
    }
}
