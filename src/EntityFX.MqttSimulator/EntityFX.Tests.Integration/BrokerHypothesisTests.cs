using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration
{
    /// <summary>
    /// Проверка гипотез H1–H5 (doc/16) на уровне эталонных данных профиля.
    /// </summary>
    [TestClass]
    public class BrokerHypothesisTests
    {
        private readonly BrokerBenchmarkRepository _repository = new();

        private MqttBrokerProfile Profile(string broker) =>
            _repository.Get(broker)!;

        private static readonly string[] AllBrokers =
            { "Mosquitto", "ActiveMQ", "Aedes", "EMQX" };

        [TestMethod]
        public void H1_Mosquitto_DegradesLatencyAndAddsFailures_AsClientsGrow()
        {
            var p = Profile("Mosquitto");

            // Резкий рост p99 на QoS 0: 0.02 -> 55.65 мс.
            var latency128 = p.Qos0.Interpolate(128).LatencyMs;
            var latency1 = p.Qos0.Interpolate(1).LatencyMs;
            Assert.IsTrue(latency128 > 20, $"Expected p99 > 20 ms at 128 clients, got {latency128}");
            Assert.IsTrue(latency128 > latency1, "Expected p99 to grow with clients");

            // Отказы на QoS 1: 64 клиента -> > 50%.
            Assert.IsTrue(p.Qos1.Interpolate(64).FailRate > 0.5,
                $"Expected Mosquitto QoS1 fail > 0.5 at 64 clients, got {p.Qos1.Interpolate(64).FailRate}");

            // При 1 клиенте Mosquitto — лидер по RPS на QoS 0.
            var mosqRps = p.Qos0.Interpolate(1).Rps;
            foreach (var broker in AllBrokers)
            {
                Assert.IsTrue(mosqRps >= Profile(broker).Qos0.Interpolate(1).Rps,
                    $"Expected Mosquitto to lead RPS at 1 client, but {broker} has higher RPS");
            }
        }

        [TestMethod]
        public void H2_ActiveMQ_HasNoFailures_OnQos1AndQos2()
        {
            var p = Profile("ActiveMQ");
            var clientCounts = new[] { 1, 2, 4, 8, 16, 32, 64, 128 };

            foreach (var n in clientCounts)
            {
                Assert.AreEqual(0.0, p.Qos1.Interpolate(n).FailRate, $"ActiveMQ QoS1 fail != 0 at {n}");
                Assert.AreEqual(0.0, p.Qos2.Interpolate(n).FailRate, $"ActiveMQ QoS2 fail != 0 at {n}");
            }

            // QoS 0: нет отказов при малом числе клиентов, появляются при большом.
            Assert.AreEqual(0.0, p.Qos0.Interpolate(1).FailRate);
            Assert.IsTrue(p.Qos0.Interpolate(128).FailRate > 0.5,
                $"Expected ActiveMQ QoS0 fail > 0.5 at 128, got {p.Qos0.Interpolate(128).FailRate}");
        }

        [TestMethod]
        public void H3_EMQX_ScalesRps_ButProducesMassiveFailures()
        {
            var p = Profile("EMQX");

            // RPS растёт с клиентами на QoS 0.
            Assert.IsTrue(p.Qos0.Interpolate(128).Rps > p.Qos0.Interpolate(1).Rps,
                "Expected EMQX QoS0 RPS to grow with clients");

            // Массовые отказы при 64 клиентах.
            Assert.IsTrue(p.Qos0.Interpolate(64).FailRate > 0.99,
                $"Expected EMQX QoS0 fail > 0.99 at 64, got {p.Qos0.Interpolate(64).FailRate}");
            Assert.IsTrue(p.Qos1.Interpolate(64).FailRate > 0.99,
                $"Expected EMQX QoS1 fail > 0.99 at 64, got {p.Qos1.Interpolate(64).FailRate}");
        }

        [TestMethod]
        public void H4_Aedes_HasLowRps_ButMinimalFailures()
        {
            var p = Profile("Aedes");

            // Ограниченная пропускная способность на QoS 0.
            Assert.IsTrue(p.Qos0.Interpolate(128).Rps < 30_000,
                $"Expected Aedes QoS0 RPS < 30000, got {p.Qos0.Interpolate(128).Rps}");

            // Нет отказов на QoS 1/2.
            var clientCounts = new[] { 1, 2, 4, 8, 16, 32, 64, 128 };
            foreach (var n in clientCounts)
            {
                Assert.AreEqual(0.0, p.Qos1.Interpolate(n).FailRate, $"Aedes QoS1 fail != 0 at {n}");
                Assert.AreEqual(0.0, p.Qos2.Interpolate(n).FailRate, $"Aedes QoS2 fail != 0 at {n}");
            }

            // Низкая доля отказов даже при 128 клиентах на QoS 0.
            Assert.IsTrue(p.Qos0.Interpolate(128).FailRate < 0.2,
                $"Expected Aedes QoS0 fail < 0.2 at 128, got {p.Qos0.Interpolate(128).FailRate}");
        }

        [TestMethod]
        public void H5_AedesIsSlowestOnQos1_ActiveMqLeadsOnQos2()
        {
            // Aedes — минимальный RPS на QoS 1 при 128 клиентах.
            var aedesRps = Profile("Aedes").Qos1.Interpolate(128).Rps;
            foreach (var broker in AllBrokers)
            {
                Assert.IsTrue(aedesRps <= Profile(broker).Qos1.Interpolate(128).Rps,
                    $"Expected Aedes to be slowest on QoS1, but {broker} is slower");
            }

            // ActiveMQ — максимальный RPS на QoS 2 при 24 клиентах.
            var activeRps = Profile("ActiveMQ").Qos2.Interpolate(24).Rps;
            foreach (var broker in AllBrokers)
            {
                Assert.IsTrue(activeRps >= Profile(broker).Qos2.Interpolate(24).Rps,
                    $"Expected ActiveMQ to lead on QoS2, but {broker} is higher");
            }
        }
    }
}