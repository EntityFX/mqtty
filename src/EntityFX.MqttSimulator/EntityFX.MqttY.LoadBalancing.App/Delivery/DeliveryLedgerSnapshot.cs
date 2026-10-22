namespace EntityFX.MqttY.LoadBalancing;

public sealed record DeliveryLedgerSnapshot(
    long MeasurementCreated,
    long UniqueDelivered,
    long DuplicateDeliveries,
    long UnexpectedDeliveries,
    long WarmupDeliveries,
    long UnknownDeliveries,
    IReadOnlyList<BackendDeliverySnapshot> Backends,
    IReadOnlyDictionary<long, long> LatencyHistogramTicks,
    double TickMilliseconds);

/// <summary>
/// Counts first deliveries of measurement-origin messages, including their drain deliveries.
/// The ledger is driven by the experiment's sequential simulation loop.
/// </summary>
