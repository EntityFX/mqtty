using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration
{
    /// <summary>Checks the legacy observations preserved in the provisional v2 fixture.</summary>
    [TestClass]
    public class BrokerHypothesisTests
    {
        private readonly BrokerBenchmarkRepository _repository = new();
        private static readonly string[] AllBrokers = { "Mosquitto", "ActiveMQ", "Aedes", "EMQX" };

        private CalibratedMqttQosSample Sample(string broker, MqttQos qos, int clients) =>
            _repository.Get(broker).For(16, qos, clients);

        [TestMethod]
        public void H1_Mosquitto_DegradesLatencyAndAddsFailures_AsClientsGrow()
        {
            var atOne = Sample("Mosquitto", MqttQos.AtLeastOnce, 1);
            var at128 = Sample("Mosquitto", MqttQos.AtLeastOnce, 128);
            Assert.IsTrue(at128.ProcessingLatencyQuantiles!.P99Ms > 10);
            Assert.IsTrue(at128.ProcessingLatencyQuantiles.P99Ms >
                atOne.ProcessingLatencyQuantiles!.P99Ms);
            Assert.IsTrue(Sample("Mosquitto", MqttQos.AtLeastOnce, 64).PublishFailureRate > 0.5);

            var mosquittoRps = Sample("Mosquitto", MqttQos.AtMostOnce, 1).CapacityRps;
            foreach (var broker in AllBrokers)
                Assert.IsTrue(mosquittoRps >= Sample(broker, MqttQos.AtMostOnce, 1).CapacityRps);
        }

        [TestMethod]
        public void H2_ActiveMQ_HasNoFailures_OnQos1AndQos2()
        {
            foreach (var clients in new[] { 1, 2, 4, 8, 16, 32, 64, 128 })
            {
                Assert.AreEqual(0, Sample("ActiveMQ", MqttQos.AtLeastOnce, clients).PublishFailureRate);
                Assert.AreEqual(0, Sample("ActiveMQ", MqttQos.ExactlyOnce, clients).PublishFailureRate);
            }
            Assert.AreEqual(0, Sample("ActiveMQ", MqttQos.AtMostOnce, 1).PublishFailureRate);
            Assert.IsTrue(Sample("ActiveMQ", MqttQos.AtMostOnce, 128).PublishFailureRate > 0.5);
        }

        [TestMethod]
        public void H3_EMQX_ScalesRps_ButProducesMassiveFailures()
        {
            Assert.IsTrue(Sample("EMQX", MqttQos.AtMostOnce, 128).CapacityRps >
                Sample("EMQX", MqttQos.AtMostOnce, 1).CapacityRps);
            Assert.IsTrue(Sample("EMQX", MqttQos.AtMostOnce, 64).PublishFailureRate > 0.99);
            Assert.IsTrue(Sample("EMQX", MqttQos.AtLeastOnce, 64).PublishFailureRate > 0.99);
        }

        [TestMethod]
        public void H4_Aedes_HasLowRps_ButMinimalFailures()
        {
            Assert.IsTrue(Sample("Aedes", MqttQos.AtMostOnce, 128).CapacityRps < 30_000);
            foreach (var clients in new[] { 1, 2, 4, 8, 16, 32, 64, 128 })
            {
                Assert.AreEqual(0, Sample("Aedes", MqttQos.AtLeastOnce, clients).PublishFailureRate);
                Assert.AreEqual(0, Sample("Aedes", MqttQos.ExactlyOnce, clients).PublishFailureRate);
            }
            Assert.IsTrue(Sample("Aedes", MqttQos.AtMostOnce, 128).PublishFailureRate < 0.2);
        }

        [TestMethod]
        public void H5_AedesIsSlowestOnQos1_ActiveMqLeadsOnQos2()
        {
            var aedesRps = Sample("Aedes", MqttQos.AtLeastOnce, 128).CapacityRps;
            foreach (var broker in AllBrokers)
                Assert.IsTrue(aedesRps <= Sample(broker, MqttQos.AtLeastOnce, 128).CapacityRps);

            var activeRps = Sample("ActiveMQ", MqttQos.ExactlyOnce, 24).CapacityRps;
            foreach (var broker in AllBrokers)
                Assert.IsTrue(activeRps >= Sample(broker, MqttQos.ExactlyOnce, 24).CapacityRps);
        }
    }
}
