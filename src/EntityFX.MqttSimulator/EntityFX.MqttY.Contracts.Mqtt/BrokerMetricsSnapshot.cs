using System.Collections.ObjectModel;

namespace EntityFX.MqttY.Contracts.Mqtt
{
    public sealed record BrokerQosMetricsSnapshot(
        long Attempted,
        long Admitted,
        long Completed,
        long ExpectedDeliveries,
        long Delivered,
        long RateRejected,
        long PublishFailed,
        long DeliveryDropped,
        double Rps,
        double? LatencyP50Ms,
        double? LatencyP95Ms,
        double? LatencyP99Ms);

    public sealed record BrokerMetricsSnapshot(
        long MeasurementStartTick,
        long MeasurementEndTick,
        IReadOnlyDictionary<MqttQos, BrokerQosMetricsSnapshot> ByQos)
    {
        public static IReadOnlyDictionary<MqttQos, BrokerQosMetricsSnapshot> ReadOnly(
            IDictionary<MqttQos, BrokerQosMetricsSnapshot> values) =>
            new ReadOnlyDictionary<MqttQos, BrokerQosMetricsSnapshot>(
                new Dictionary<MqttQos, BrokerQosMetricsSnapshot>(values));
    }
}
