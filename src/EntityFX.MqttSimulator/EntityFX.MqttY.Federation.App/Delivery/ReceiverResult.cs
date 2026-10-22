namespace EntityFX.MqttY.Federation;

public sealed record ReceiverResult(int Site, long UniqueDeliveries, long Duplicates, IReadOnlyDictionary<long, long> LatencyHistogramTicks);
