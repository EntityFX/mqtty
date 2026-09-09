using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class BrokerProfileV2Tests
    {
        private static LatencyQuantiles Latency(double value) =>
            new(value, value + 1, value + 2, value + 3, value + 4, value + 5);

        private static MqttBrokerProfile Profile() => new()
        {
            BrokerType = "Test",
            MessageSizes = new Dictionary<int, MqttMessageSizeProfile>
            {
                [16] = new(16, new Dictionary<MqttQos, CalibratedMqttQosProfile>
                {
                    [MqttQos.AtMostOnce] = new(new[]
                    {
                        new CalibratedMqttQosSample(1, 100, 0.1, 0.2, null),
                        new CalibratedMqttQosSample(3, 300, 0.3, 0.4, null)
                    }),
                    [MqttQos.AtLeastOnce] = new(new[]
                    {
                        new CalibratedMqttQosSample(1, 50, 0.0, 0.1, Latency(1)),
                        new CalibratedMqttQosSample(3, 150, 0.2, 0.3, Latency(3))
                    }),
                    [MqttQos.ExactlyOnce] = new(new[]
                    {
                        new CalibratedMqttQosSample(1, 25, 0.0, 0.0, Latency(2)),
                        new CalibratedMqttQosSample(3, 75, 0.1, 0.2, Latency(4))
                    })
                }),
                [256] = new(256, new Dictionary<MqttQos, CalibratedMqttQosProfile>
                {
                    [MqttQos.AtMostOnce] = new(new[] { new CalibratedMqttQosSample(1, 80, 0, 0, null) }),
                    [MqttQos.AtLeastOnce] = new(new[] { new CalibratedMqttQosSample(1, 40, 0, 0, Latency(4)) }),
                    [MqttQos.ExactlyOnce] = new(new[] { new CalibratedMqttQosSample(1, 20, 0, 0, Latency(5)) })
                })
            }
        };

        [TestMethod]
        public void For_UsesExactMessageDimensionAndInterpolatesClients()
        {
            var profile = Profile();
            profile.Validate();

            var sample = profile.For(16, MqttQos.AtLeastOnce, 2);
            Assert.AreEqual(100.0, sample.CapacityRps);
            Assert.AreEqual(0.1, sample.PublishFailureRate);
            Assert.AreEqual(0.2, sample.ConditionalDeliveryLossRate);
            Assert.AreEqual(2.0, sample.ProcessingLatencyQuantiles!.MinMs);
            Assert.AreEqual(7.0, sample.ProcessingLatencyQuantiles.MaxMs);
            Assert.AreEqual(80.0, profile.For(256, MqttQos.AtMostOnce, 1).CapacityRps);
        }

        [TestMethod]
        public void For_FailsForUnknownDimensionOrInvalidClients()
        {
            var profile = Profile();
            Assert.ThrowsException<KeyNotFoundException>(() =>
                profile.For(32, MqttQos.AtLeastOnce, 1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                profile.For(16, MqttQos.AtLeastOnce, 0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                profile.For(16, (MqttQos)99, 1));
        }

        [TestMethod]
        public void Repository_RejectsV1AndUnknownBroker()
        {
            Assert.ThrowsException<InvalidDataException>(() =>
                new BrokerBenchmarkRepository("{\"schemaVersion\":1,\"brokers\":[]}"));

            var repository = new BrokerBenchmarkRepository();
            Assert.ThrowsException<KeyNotFoundException>(() => repository.Get("Unknown"));
        }

        [TestMethod]
        public void Validate_RejectsDuplicateClientsAndInvalidQuantiles()
        {
            var duplicate = Profile();
            duplicate.MessageSizes[16].Qos[MqttQos.AtLeastOnce] = new CalibratedMqttQosProfile(new[]
            {
                new CalibratedMqttQosSample(1, 1, 0, 0, Latency(1)),
                new CalibratedMqttQosSample(1, 2, 0, 0, Latency(1))
            });
            Assert.ThrowsException<InvalidDataException>(() => duplicate.Validate());

            var invalidLatency = Profile();
            invalidLatency.MessageSizes[16].Qos[MqttQos.ExactlyOnce] =
                new CalibratedMqttQosProfile(new[]
                {
                    new CalibratedMqttQosSample(1, 1, 0, 0,
                        new LatencyQuantiles(0, 2, 1, 3, 4, 5))
                });
            Assert.ThrowsException<InvalidDataException>(() => invalidLatency.Validate());
        }

        [TestMethod]
        public void Validate_RejectsInvalidCapacityRatesAndMissingQos()
        {
            var invalidCapacity = Profile();
            invalidCapacity.MessageSizes[16].Qos[MqttQos.AtLeastOnce] =
                new CalibratedMqttQosProfile(new[]
                {
                    new CalibratedMqttQosSample(1, double.NaN, 0, 0, Latency(1))
                });
            Assert.ThrowsException<InvalidDataException>(() => invalidCapacity.Validate());

            var invalidRate = Profile();
            invalidRate.MessageSizes[16].Qos[MqttQos.ExactlyOnce] =
                new CalibratedMqttQosProfile(new[]
                {
                    new CalibratedMqttQosSample(1, 1, 1.01, 0, Latency(1))
                });
            Assert.ThrowsException<InvalidDataException>(() => invalidRate.Validate());

            var missingQos = Profile();
            missingQos.MessageSizes[16].Qos.Remove(MqttQos.ExactlyOnce);
            Assert.ThrowsException<InvalidDataException>(() => missingQos.Validate());
        }

        [TestMethod]
        public void Validate_EnforcesQosLatencyApplicability()
        {
            var qos0Latency = Profile();
            qos0Latency.MessageSizes[16].Qos[MqttQos.AtMostOnce] =
                new CalibratedMqttQosProfile(new[]
                {
                    new CalibratedMqttQosSample(1, 1, 0, 0, Latency(1))
                });
            Assert.ThrowsException<InvalidDataException>(() => qos0Latency.Validate());

            var qos1MissingLatency = Profile();
            qos1MissingLatency.MessageSizes[16].Qos[MqttQos.AtLeastOnce] =
                new CalibratedMqttQosProfile(new[]
                {
                    new CalibratedMqttQosSample(1, 1, 0, 0, null)
                });
            Assert.ThrowsException<InvalidDataException>(() => qos1MissingLatency.Validate());
        }

        [DataTestMethod]
        [DataRow(0.0, 0.0, 0.0)]
        [DataRow(0.2, 0.5, 0.375)]
        [DataRow(1.0, 1.0, 0.0)]
        public void ConditionalLoss_UsesIndependentFailureFormula(
            double rateFailure, double targetFailure, double expected)
        {
            Assert.AreEqual(expected,
                CalibrationMath.ConditionalDeliveryLoss(rateFailure, targetFailure), 1e-12);
        }

        [TestMethod]
        public void ConditionalLoss_RejectsCapacityFailureAboveTarget()
        {
            Assert.ThrowsException<InvalidOperationException>(() =>
                CalibrationMath.ConditionalDeliveryLoss(0.6, 0.5));
        }

        [TestMethod]
        public void DeterministicSampler_IsStableAndSeparatesPurposes()
        {
            var first = DeterministicMqttSampler.Uniform(42, "broker", "client", 7, "latency");
            var repeated = DeterministicMqttSampler.Uniform(42, "broker", "client", 7, "latency");
            var otherPurpose = DeterministicMqttSampler.Uniform(42, "broker", "client", 7, "publish-failure");

            Assert.AreEqual(first, repeated);
            Assert.AreNotEqual(first, otherPurpose);
            Assert.IsTrue(first >= 0 && first <= 1);
        }

        [DataTestMethod]
        [DataRow(0.00, 0.0)]
        [DataRow(0.50, 10.0)]
        [DataRow(0.75, 20.0)]
        [DataRow(0.95, 30.0)]
        [DataRow(0.99, 40.0)]
        [DataRow(1.00, 50.0)]
        [DataRow(0.625, 15.0)]
        public void InverseCdf_InterpolatesBetweenQuantileKnots(double uniform, double expected)
        {
            var quantiles = new LatencyQuantiles(0, 10, 20, 30, 40, 50);
            Assert.AreEqual(expected, DeterministicMqttSampler.InverseCdf(uniform, quantiles), 1e-12);
        }
    }
}
