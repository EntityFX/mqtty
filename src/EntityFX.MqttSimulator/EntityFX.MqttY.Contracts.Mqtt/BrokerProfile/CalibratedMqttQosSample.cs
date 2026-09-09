namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    public sealed record CalibratedMqttQosSample(
        int Clients,
        double CapacityRps,
        double PublishFailureRate,
        double ConditionalDeliveryLossRate,
        LatencyQuantiles? ProcessingLatencyQuantiles)
    {
        public double? AttemptedRps { get; init; }
        public double? TargetCompletedRps { get; init; }
        public double? TargetPublishFailureRate { get; init; }
        public LatencyQuantiles? ObservedLatencyQuantiles { get; init; }
        public double? RttBaselineMs { get; init; }
    }
}
