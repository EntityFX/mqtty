using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Admission;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    [TestCategory(TestCategories.Mechanism)]
    public class BrokerAdmissionTests
    {
        private static PublishAdmissionContext Context(long tick = 10, long sequence = 1,
            int messageBytes = 16, MqttQos qos = MqttQos.AtLeastOnce) =>
            new("pub", qos, 1, messageBytes, sequence, tick, ClientCount: 1);

        [TestMethod]
        public void NoopAdmission_IsImmediateAndDoesNotLimit()
        {
            var admission = NoopBrokerAdmissionModel.Instance.TryAdmit(Context());

            Assert.IsTrue(admission.Admit);
            Assert.IsTrue(admission.Immediate);
            Assert.AreEqual(0, admission.DelayTicks);
            Assert.AreEqual(BrokerRejectReason.None, admission.RejectReason);
        }

        [TestMethod]
        public void ProfileAdmission_RejectsProbabilisticFailure()
        {
            var model = new ProfileBrokerAdmissionModel(
                BrokerProfileTestData.Create("reject-all", 16, 1_000_000, publishFailureRate: 1),
                TimeSpan.FromMilliseconds(0.1), randomSeed: 42, brokerName: "mqs1", profileClientCount: 1,
                protocolFloorTicks: () => null, capacityPacing: () => false);

            var admission = model.TryAdmit(Context());

            Assert.IsFalse(admission.Admit);
            Assert.AreEqual(BrokerRejectReason.PublishFailure, admission.RejectReason);
        }

        [TestMethod]
        public void ProfileAdmission_RejectsWhenRateBudgetIsEmpty()
        {
            var model = new ProfileBrokerAdmissionModel(
                BrokerProfileTestData.Create("low-rps", 16, capacityRps: 1),
                TimeSpan.FromSeconds(1), randomSeed: 42, brokerName: "mqs1", profileClientCount: 1,
                protocolFloorTicks: () => null, capacityPacing: () => false);

            Assert.AreEqual(BrokerRejectReason.RateLimit, model.TryAdmit(Context(tick: 0)).RejectReason);
            Assert.IsTrue(model.TryAdmit(Context(tick: 1)).Admit,
                "One token arrives after one full tick.");
        }

        [TestMethod]
        public void ProfileAdmission_CarriesDelayAndConditionalLoss()
        {
            var model = new ProfileBrokerAdmissionModel(
                BrokerProfileTestData.Create("accepted", 16, 1_000_000,
                    deliveryLossRate: 0.25, latencyMs: 0.1),
                TimeSpan.FromMilliseconds(0.1), randomSeed: 42, brokerName: "mqs1", profileClientCount: 1,
                protocolFloorTicks: () => null, capacityPacing: () => false);

            var admission = model.TryAdmit(Context());

            Assert.IsTrue(admission.Admit);
            Assert.IsFalse(admission.Immediate);
            Assert.IsTrue(admission.DelayTicks >= 1);
            Assert.AreEqual(0.25, admission.ConditionalDeliveryLossRate, 1e-12);
        }
    }
}
