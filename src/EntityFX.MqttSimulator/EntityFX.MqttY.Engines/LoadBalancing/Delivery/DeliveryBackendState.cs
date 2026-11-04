namespace EntityFX.MqttY.LoadBalancing;

internal sealed class DeliveryBackendState
{
    public long MeasurementCreated;
    public long UniqueDelivered;
    public long DuplicateDeliveries;
    public long UnexpectedDeliveries;
    public SortedDictionary<long, long> LatencyHistogramTicks { get; } = new();
}
