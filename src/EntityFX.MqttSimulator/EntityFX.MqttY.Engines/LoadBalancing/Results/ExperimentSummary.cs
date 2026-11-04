namespace EntityFX.MqttY.LoadBalancing;

public sealed record ExperimentSummary(string Status, string? Error, double ConfiguredOfferedRps,
    long ScheduledPublishes, long ApiAttempts, long SendRejected, long BackpressureUnsent,
    int PublisherPendingBeforeDrain, int PublisherPendingAfterDrain, long MeasurementStartTick,
    long MeasurementEndTick, long FinalTick, long GraphErrors, double MeasurementSeconds,
    double TickMilliseconds, double? BackendCompletedRps, double? UniqueGoodput,
    double? AttemptDeliveryLoss, double? BackendPublishFailureRate, double? BackendDeliveryLossRate,
    double? PublishP50Ms, double? PublishP95Ms, double? PublishP99Ms,
    double? ReceiverP50Ms, double? ReceiverP95Ms, double? ReceiverP99Ms,
    long UniqueReceivedBeforeDrain, long UniqueReceivedDuringDrain,
    DeliveryLedgerSnapshot Delivery, IReadOnlyList<BackendResult> Backends,
    IReadOnlyDictionary<long, long> PublishLatencyHistogramTicks, IReadOnlyList<PhaseResult> Phases);
