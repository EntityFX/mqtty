using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration
{
    internal static class BrokerProfileTestData
    {
        public static MqttBrokerProfile Create(
            string brokerType, int messageBytes, double capacityRps,
            double publishFailureRate = 0, double deliveryLossRate = 0,
            double latencyMs = 0)
        {
            var latency = new LatencyQuantiles(
                latencyMs, latencyMs, latencyMs, latencyMs, latencyMs, latencyMs);
            return new MqttBrokerProfile
            {
                BrokerType = brokerType,
                MessageSizes = new Dictionary<int, MqttMessageSizeProfile>
                {
                    [messageBytes] = new(messageBytes,
                        new Dictionary<MqttQos, CalibratedMqttQosProfile>
                        {
                            [MqttQos.AtMostOnce] = new(new[]
                            {
                                new CalibratedMqttQosSample(1, capacityRps,
                                    publishFailureRate, deliveryLossRate, null)
                            }),
                            [MqttQos.AtLeastOnce] = new(new[]
                            {
                                new CalibratedMqttQosSample(1, capacityRps,
                                    publishFailureRate, deliveryLossRate, latency)
                            }),
                            [MqttQos.ExactlyOnce] = new(new[]
                            {
                                new CalibratedMqttQosSample(1, capacityRps,
                                    publishFailureRate, deliveryLossRate, latency)
                            })
                        })
                }
            };
        }
    }
}
