using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class MqttQosProfileTests
    {
        private static CalibratedMqttQosProfile CreateProfile() => new(new[]
        {
            new CalibratedMqttQosSample(1, 100, 0, 0.1,
                new LatencyQuantiles(1, 2, 3, 4, 5, 6)),
            new CalibratedMqttQosSample(10, 1000, 0.5, 0.2,
                new LatencyQuantiles(10, 20, 30, 40, 50, 60))
        });

        [TestMethod]
        public void Interpolate_ClampsOutsideRange()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => CreateProfile().Interpolate(0));
            Assert.AreEqual(1000, CreateProfile().Interpolate(100).CapacityRps);
        }

        [TestMethod]
        public void Interpolate_Midpoint_ReturnsLinearValues()
        {
            var sample = CreateProfile().Interpolate(5);
            var ratio = 4.0 / 9.0;
            Assert.AreEqual(100 + ratio * 900, sample.CapacityRps, 1e-9);
            Assert.AreEqual(ratio * 0.5, sample.PublishFailureRate, 1e-9);
            Assert.AreEqual(1 + ratio * 9, sample.ProcessingLatencyQuantiles!.MinMs, 1e-9);
        }

        [TestMethod]
        public void Repository_LoadsV2DimensionsForAllFourBrokers()
        {
            var repository = new BrokerBenchmarkRepository();
            foreach (var broker in new[] { "Mosquitto", "ActiveMQ", "Aedes", "EMQX" })
            {
                var profile = repository.Get(broker);
                Assert.AreEqual(11, profile.MessageSizes[16].Qos[MqttQos.AtMostOnce].Samples.Count);
                Assert.AreEqual(11, profile.MessageSizes[256].Qos[MqttQos.ExactlyOnce].Samples.Count);
            }
        }

        [TestMethod]
        public void Repository_MatchesProvisionalLegacyReferenceValue()
        {
            var sample = new BrokerBenchmarkRepository().Get("Mosquitto")
                .For(16, MqttQos.AtMostOnce, 1);
            Assert.AreEqual(57087.5, sample.CapacityRps, 1e-6);
            Assert.IsNull(sample.ProcessingLatencyQuantiles);
        }
    }
}
