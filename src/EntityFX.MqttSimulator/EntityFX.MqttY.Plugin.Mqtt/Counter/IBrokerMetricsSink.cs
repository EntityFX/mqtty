using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Plugin.Mqtt.Counter
{
    /// <summary>
    /// Broker-side measurement contract. The protocol layer reports attempts,
    /// admissions and deliveries; the implementation owns locking, keying and
    /// histogram/quantile details.
    /// </summary>
    internal interface IBrokerMetricsSink
    {
        BrokerMeasurement.PublishTracker BeginAttempt(
            string publisher, MqttQos qos, ushort? packetId, long networkPacketId, long startTick,
            long localPublishSequence = 0, int messageBytes = 0);

        void Admit(BrokerMeasurement.PublishTracker tracker);

        void RejectByRate(BrokerMeasurement.PublishTracker tracker);

        void FailPublish(BrokerMeasurement.PublishTracker tracker);

        BrokerMeasurement.PublishTracker? Find(
            string publisher, MqttQos qos, ushort? packetId, long networkPacketId);

        void ExpectDelivery(BrokerMeasurement.PublishTracker? tracker, long outgoingPacketId);

        void DeliveryCompleted(long outgoingPacketId);

        void DeliveryDropped(long outgoingPacketId);

        long? CompletePublisher(string publisher, MqttQos qos, ushort packetId, long endTick);

        void CompleteFanOut(BrokerMeasurement.PublishTracker? tracker);

        void TrackPublishers(IEnumerable<string> names);

        BrokerQosMetricsSnapshot PublisherSnapshot(string name, MqttQos qos, long endTick, TimeSpan tickPeriod);

        void Reset(long startTick);

        BrokerMetricsSnapshot Snapshot(long endTick, TimeSpan tickPeriod);
    }
}
