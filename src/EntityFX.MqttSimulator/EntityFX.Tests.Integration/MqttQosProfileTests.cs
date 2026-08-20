using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class MqttQosProfileTests
    {
        private static MqttQosProfile CreateProfile() => new()
        {
            Samples = new[]
            {
                new MqttQosSample(1,   100.0, 1.0, 0.0),
                new MqttQosSample(10, 1000.0, 10.0, 0.5),
            }
        };

        [TestMethod]
        public void Interpolate_BelowRange_ReturnsFirstSample()
        {
            var s = CreateProfile().Interpolate(0);
            Assert.AreEqual(100.0, s.Rps);
            Assert.AreEqual(1.0, s.LatencyMs);
            Assert.AreEqual(0.0, s.FailRate);
        }

        [TestMethod]
        public void Interpolate_AboveRange_ReturnsLastSample()
        {
            var s = CreateProfile().Interpolate(100);
            Assert.AreEqual(1000.0, s.Rps);
            Assert.AreEqual(10.0, s.LatencyMs);
            Assert.AreEqual(0.5, s.FailRate);
        }

        [TestMethod]
        public void Interpolate_Midpoint_ReturnsLinearValue()
        {
            var s = CreateProfile().Interpolate(5);

            // t = (5 - 1) / (10 - 1) = 4 / 9
            var expectedRps = 100.0 + (4.0 / 9.0) * 900.0;
            var expectedLatency = 1.0 + (4.0 / 9.0) * 9.0;
            var expectedFail = 0.0 + (4.0 / 9.0) * 0.5;

            Assert.AreEqual(expectedRps, s.Rps, 1e-9);
            Assert.AreEqual(expectedLatency, s.LatencyMs, 1e-9);
            Assert.AreEqual(expectedFail, s.FailRate, 1e-9);
        }

        [TestMethod]
        public void Repository_LoadsAllFourBrokers()
        {
            var repository = new BrokerBenchmarkRepository();

            Assert.IsNotNull(repository.Get("Mosquitto"));
            Assert.IsNotNull(repository.Get("ActiveMQ"));
            Assert.IsNotNull(repository.Get("Aedes"));
            Assert.IsNotNull(repository.Get("EMQX"));
        }

        [TestMethod]
        public void Repository_ProfilesContainElevenSamples()
        {
            var repository = new BrokerBenchmarkRepository();

            var profile = repository.Get("Mosquitto")!;

            Assert.AreEqual(11, profile.Qos0.Samples.Count);
            Assert.AreEqual(11, profile.Qos1.Samples.Count);
            Assert.AreEqual(11, profile.Qos2.Samples.Count);
        }

        [TestMethod]
        public void Repository_MatchesBenchmarkReferenceValue()
        {
            var repository = new BrokerBenchmarkRepository();

            var sample = repository.Get("Mosquitto")!.Qos0.Interpolate(1);

            Assert.AreEqual(57087.5, sample.Rps, 1e-6);
        }

        [TestMethod]
        public void BrokerProfile_ForQos_MapsExpectedLevel()
        {
            var profile = new MqttBrokerProfile
            {
                Qos0 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 1.0, 0.1, 0.0) } },
                Qos1 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 2.0, 0.2, 0.0) } },
                Qos2 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 3.0, 0.3, 0.0) } },
            };

            Assert.AreEqual(1.0, profile.ForQos(MqttQos.AtMostOnce).Samples[0].Rps);
            Assert.AreEqual(2.0, profile.ForQos(MqttQos.AtLeastOnce).Samples[0].Rps);
            Assert.AreEqual(3.0, profile.ForQos(MqttQos.ExactlyOnce).Samples[0].Rps);
        }
    }
}