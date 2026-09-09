namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    public sealed record LatencyQuantiles(
        double MinMs,
        double P50Ms,
        double P75Ms,
        double P95Ms,
        double P99Ms,
        double MaxMs);
}
