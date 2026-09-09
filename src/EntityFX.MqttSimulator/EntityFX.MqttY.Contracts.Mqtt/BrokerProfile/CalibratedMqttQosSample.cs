namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    public sealed record CalibratedMqttQosSample(
        int Clients,
        double CapacityRps,
        double PublishFailureRate,
        double ConditionalDeliveryLossRate,
        LatencyQuantiles? ProcessingLatencyQuantiles);
}
