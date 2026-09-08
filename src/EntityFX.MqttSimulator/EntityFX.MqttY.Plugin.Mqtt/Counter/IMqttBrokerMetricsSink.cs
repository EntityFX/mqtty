using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Plugin.Mqtt.Counter
{
    internal interface IMqttBrokerMetricsSink
    {
        void CompletePublisher(string publisher, MqttQos qos, ushort packetId, long tick);

        void CompleteDelivery(long outgoingPublishPacketId);
    }
}
