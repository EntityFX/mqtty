using System.Text.Json;
using EntityFX.MqttY.Federation;
using EntityFX.MqttY.LoadBalancing;
using EntityFX.MqttY.Results;

namespace EntityFX.MqttY.Engines.Execution;

/// <summary>Scientific core metrics: counts, throughput and publication latency.</summary>
public sealed record CoreMetrics(
    string Status,
    long Scheduled,
    long Attempts,
    long Rejected,
    long Unique,
    double Goodput,
    double? PublishP50Ms,
    double? PublishP95Ms,
    double? PublishP99Ms,
    long Errors);

/// <summary>Receiver, duplicate and overload diagnostics; not part of the strict gate.</summary>
public sealed record DiagnosticMetrics(
    double? ReceiverP50Ms,
    double? ReceiverP95Ms,
    double? ReceiverP99Ms,
    long Duplicates,
    long Unexpected,
    long Unknown,
    long QueueDrops,
    long ForwardRejected,
    long HopLimitDrops);

public sealed record CollectedMetrics(CoreMetrics Core, DiagnosticMetrics Diagnostics);

/// <summary>
/// Shared counter collector for load drivers: scheduled/attempted/rejected/
/// unique/errors plus publication latency quantiles. Snapshotting is the only
/// operation that sorts samples; recording is atomic and allocation-free.
/// </summary>
public sealed class MetricsCollector
{
    private readonly List<long> _latencyTicks = new();
    private long _scheduled;
    private long _attempts;
    private long _rejected;
    private long _unique;
    private long _errors;

    public long Scheduled => Interlocked.Read(ref _scheduled);
    public long Attempts => Interlocked.Read(ref _attempts);
    public long Rejected => Interlocked.Read(ref _rejected);
    public long Unique => Interlocked.Read(ref _unique);
    public long Errors => Interlocked.Read(ref _errors);

    public void RecordScheduled(long count) => Interlocked.Add(ref _scheduled, count);

    public void RecordAttempt() => Interlocked.Increment(ref _attempts);

    public void RecordRejected() => Interlocked.Increment(ref _rejected);

    public void RecordUnique(long count) => Interlocked.Add(ref _unique, count);

    public void RecordError() => Interlocked.Increment(ref _errors);

    public void RecordLatencyTicks(long ticks)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        lock (_latencyTicks)
        {
            _latencyTicks.Add(ticks);
        }
    }

    public CoreMetrics Snapshot(string status, double measurementSeconds, TimeSpan tickPeriod)
    {
        var quantiles = SnapshotQuantiles();
        double? Ms(double? ticks) => ticks * tickPeriod.TotalMilliseconds;

        return new CoreMetrics(
            status,
            Scheduled,
            Attempts,
            Rejected,
            Unique,
            measurementSeconds > 0 ? Unique / measurementSeconds : 0,
            Ms(quantiles.P50),
            Ms(quantiles.P95),
            Ms(quantiles.P99),
            Errors);
    }

    public static CollectedMetrics FromLoadBalancing(ExperimentSummary summary) => new(
        new CoreMetrics(summary.Status, summary.ScheduledPublishes, summary.ApiAttempts,
            summary.SendRejected, summary.Delivery.UniqueDelivered, summary.UniqueGoodput ?? 0,
            summary.PublishP50Ms, summary.PublishP95Ms, summary.PublishP99Ms, summary.GraphErrors),
        new DiagnosticMetrics(summary.ReceiverP50Ms, summary.ReceiverP95Ms, summary.ReceiverP99Ms,
            summary.Delivery.DuplicateDeliveries, summary.Delivery.UnexpectedDeliveries,
            summary.Delivery.UnknownDeliveries, 0, 0, 0));

    public static CollectedMetrics FromFederation(FederationSummary summary) => new(
        new CoreMetrics(summary.Status, summary.ScheduledPublishes, summary.ApiAttempts,
            summary.SendRejected, summary.UniqueDeliveries, summary.UniqueGoodput ?? 0,
            summary.PublishP50Ms, summary.PublishP95Ms, summary.PublishP99Ms, 0),
        new DiagnosticMetrics(summary.ReceiverP50Ms, summary.ReceiverP95Ms, summary.ReceiverP99Ms,
            0, 0, summary.UnknownDeliveries, summary.QueueDrops, summary.ForwardRejected,
            summary.HopLimitDrops));

    public static string Serialize(CoreMetrics metrics) =>
        JsonSerializer.Serialize(metrics, JsonPolicies.Result);

    public static string Serialize(DiagnosticMetrics metrics) =>
        JsonSerializer.Serialize(metrics, JsonPolicies.Result);

    private (long? P50, long? P95, long? P99) SnapshotQuantiles()
    {
        long[] ticks;
        lock (_latencyTicks)
        {
            if (_latencyTicks.Count == 0) return (null, null, null);
            ticks = _latencyTicks.ToArray();
        }

        Array.Sort(ticks);
        long At(double quantile)
        {
            var rank = Math.Clamp((long)Math.Ceiling(quantile * ticks.Length), 1, ticks.Length);
            return ticks[rank - 1];
        }

        return (At(0.50), At(0.95), At(0.99));
    }
}
