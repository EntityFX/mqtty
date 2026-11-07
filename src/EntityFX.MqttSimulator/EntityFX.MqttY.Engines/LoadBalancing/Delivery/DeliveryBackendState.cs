namespace EntityFX.MqttY.LoadBalancing;

internal sealed class DeliveryBackendState
{
    /// <summary>Guards the histogram and snapshot reads for this backend only.</summary>
    public object Sync { get; } = new();
    public long MeasurementCreated;
    public long UniqueDelivered;
    public long DuplicateDeliveries;
    public long UnexpectedDeliveries;
    public SortedDictionary<long, long> LatencyHistogramTicks { get; } = new();
}
