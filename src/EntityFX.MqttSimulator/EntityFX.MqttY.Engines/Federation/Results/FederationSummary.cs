namespace EntityFX.MqttY.Federation;

public sealed record FederationSummary(string Status, string? Error, double MeasurementSeconds, double TickMilliseconds,
    long ScheduledPublishes, long ApiAttempts, long SendRejected, long BackpressureUnsent,
    long SourceCompleted, long SourceCompletedBeforeDrain, long SourceCompletedDuringDrain, double? SourceCompletedRps,
    long ExpectedDeliveries, long UniqueDeliveries, long UniqueBeforeDrain, long UniqueDuringDrain,
    long? FullyDeliveredPublications, double? DeliveryLoss, double? UniqueGoodput,
    long DuplicateDeliveries, long UnexpectedDeliveries, long UnknownDeliveries,
    long ForwardAttempts, long ForwardAccepted, long ForwardRejected, long ForwardTopicPayloadBytes,
    long SuppressedDuplicates, long QueueDrops, long HopLimitDrops, long CacheEntries,
    double? PublishP50Ms, double? PublishP95Ms, double? PublishP99Ms,
    double? ReceiverP50Ms, double? ReceiverP95Ms, double? ReceiverP99Ms,
    SourcePublishResult[] Sources, ReceiverResult[] Receivers, IReadOnlyDictionary<long, long> PublishHistogramTicks,
    IReadOnlyDictionary<long, long> ReceiverHistogramTicks, FederationPhase[] Phases);
