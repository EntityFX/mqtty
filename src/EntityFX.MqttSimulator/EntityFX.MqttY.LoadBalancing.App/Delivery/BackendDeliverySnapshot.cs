namespace EntityFX.MqttY.LoadBalancing;

public sealed record BackendDeliverySnapshot(
    int Backend,
    long MeasurementCreated,
    long UniqueDelivered,
    long DuplicateDeliveries,
    long UnexpectedDeliveries,
    IReadOnlyDictionary<long, long> LatencyHistogramTicks);
