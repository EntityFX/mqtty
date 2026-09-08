using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Counter;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Counter;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;

namespace EntityFX.Tests.Integration
{
    /// <summary>
    /// Верификация фактических метрик брокера по счётчикам (L2):
    /// раздельный учёт обработанных сообщений, отказов по перегрузке и
    /// вероятностных отказов по уровням QoS.
    /// </summary>
    [TestClass]
    public class BrokerMetricsValidationTests
    {
        private static TicksOptions Ticks => new()
        {
            TickPeriod = TimeSpan.FromMilliseconds(0.1),
            OutgoingWaitTicks = 1,
            ReceiveWaitPeriod = TimeSpan.FromSeconds(30),
            CounterHistoryDepth = 1000
        };

        private static MqttBrokerProfile RejectAllProfile() => new()
        {
            BrokerType = "Validation-RejectAll",
            // Высокий RPS, чтобы лимитер не отсекал публикации до вероятностного отказа.
            Qos0 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 1_000_000.0, 0.0, 1.0) } },
            Qos1 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 1_000_000.0, 0.0, 1.0) } },
            Qos2 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 1_000_000.0, 0.0, 1.0) } },
        };

        private static MqttBrokerProfile VeryLowRpsProfile() => new()
        {
            BrokerType = "Validation-LowRps",
            Qos0 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 1.0, 0.0, 0.0) } },
            Qos1 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 1.0, 0.0, 0.0) } },
            Qos2 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 1.0, 0.0, 0.0) } },
        };

        private static (NetworkSimulator Graph, MqttClient Publisher, IMqttBroker Broker) Build(MqttBrokerProfile? profile)
        {
            var ticks = Ticks;
            var pathFinder = new DijkstraPathFinder();
            var monitoring = new NetworkLogger(false, TimeSpan.FromMilliseconds(0.1),
                new MonitoringIgnoreOption { Category = new[] { "Refresh", "Link" } });

            var graph = new NetworkSimulator(pathFinder, monitoring, ticks, true);

            var mqttTopicEvaluator = new MqttTopicEvaluator(true);
            var mqttPacketManager = new MqttNativePacketManager(mqttTopicEvaluator);

            var network = new Network(0, "net1", "net1.local", "eth",
                new NetworkOptions { NetworkType = "eth", TransferTicks = 1, Speed = 18750000 },
                ticks, true);
            graph.AddNetwork(network);

            var broker = new MqttBroker(mqttPacketManager, mqttTopicEvaluator,
                0, "mqs1", "mqtt://mqs1.net1.local", "mqtt", "mqtt-server",
                ticks, true, profile);
            network.AddServer(broker);
            graph.AddServer(broker);

            var publisher = new MqttClient(mqttPacketManager, 1, "pub", "mqtt://pub.net1.local",
                "mqtt", "mqtt-client", "pub", ticks, true);
            network.AddClient(publisher);
            graph.AddClient(publisher);

            return (graph, publisher, broker);
        }

        private static MqttCounters? GetMqttCounters(INode node) =>
            (node.Counters as NodeCounters)?.Counters.OfType<MqttCounters>().FirstOrDefault();

        private static void Refresh(NetworkSimulator graph, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                graph.Refresh(false, 0);
            }
        }

        [TestMethod]
        public void RejectAllProfile_RefusesAllPublishes_ByFailRate()
        {
            var (graph, publisher, broker) = Build(RejectAllProfile());

            publisher.BeginConnect("mqs1");
            Refresh(graph, 2_000);
            Assert.IsTrue(publisher.IsConnected, "Publisher must connect");
            graph.ResetMeasurement();

            const int total = 100;
            for (var i = 0; i < total; i++)
            {
                publisher.Publish("test/data", new byte[] { 1 }, MqttQos.AtLeastOnce);
                // Каждая публикация приходит в отдельном тике, чтобы высокий RPS не
                // вытеснял её лимитером и работала вероятностная модель отказа.
                Refresh(graph, 20);
            }

            Refresh(graph, 2_000);

            var counters = GetMqttCounters(broker)!;
            var accepted = counters.PublishByQos[MqttQos.AtLeastOnce].Value;
            var refusedByFail = counters.RefusedByFailRate[MqttQos.AtLeastOnce].Value;
            var refusedByRate = counters.RefusedByRateLimit[MqttQos.AtLeastOnce].Value;

            Assert.AreEqual(0, accepted, "Reject-all profile must not accept any publish");
            Assert.IsTrue(refusedByFail > 0, "Fail-rate model must refuse the majority of publishes");
            Assert.AreEqual(total, refusedByFail + refusedByRate,
                "Every publish must be either refused (fail/rate) or accepted");

            var metrics = broker.GetMetrics().ByQos[MqttQos.AtLeastOnce];
            Assert.AreEqual(total, metrics.Attempted);
            Assert.AreEqual(0L, metrics.Admitted);
            Assert.AreEqual(0L, metrics.Completed);
            Assert.IsTrue(metrics.PublishFailed > 0);
            Assert.AreEqual(total, metrics.PublishFailed + metrics.RateRejected);
        }

        [TestMethod]
        public void LowRpsProfile_ProducesRateLimitRefusals()
        {
            var (graph, publisher, broker) = Build(VeryLowRpsProfile());

            publisher.BeginConnect("mqs1");
            Refresh(graph, 2_000);
            Assert.IsTrue(publisher.IsConnected, "Publisher must connect");
            graph.ResetMeasurement();

            const int total = 200;
            for (var i = 0; i < total; i++)
            {
                publisher.Publish("test/data", new byte[] { 1 }, MqttQos.AtLeastOnce);
                Refresh(graph, 1);
            }

            Refresh(graph, 2_000);

            var counters = GetMqttCounters(broker)!;
            var accepted = counters.PublishByQos[MqttQos.AtLeastOnce].Value;
            var refusedByRate = counters.RefusedByRateLimit[MqttQos.AtLeastOnce].Value;

            Assert.IsTrue(accepted < total - 1, "Low-RPS profile must reject the excess");
            Assert.IsTrue(refusedByRate > 0, "Rate limiter must produce refusals");

            var metrics = broker.GetMetrics().ByQos[MqttQos.AtLeastOnce];
            Assert.AreEqual(total, metrics.Attempted);
            Assert.AreEqual(0L, metrics.PublishFailed);
            Assert.IsTrue(metrics.RateRejected > 0);
            Assert.AreEqual(metrics.Attempted, metrics.Admitted + metrics.RateRejected);
        }
    }
}
