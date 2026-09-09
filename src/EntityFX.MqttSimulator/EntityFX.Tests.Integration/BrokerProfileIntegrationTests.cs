using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    [TestCategory(TestCategories.Regression)]
    public class BrokerProfileIntegrationTests
    {
        private static TicksOptions Ticks => new()
        {
            TickPeriod = TimeSpan.FromMilliseconds(0.1),
            OutgoingWaitTicks = 1,
            ReceiveWaitPeriod = TimeSpan.FromSeconds(30),
            CounterHistoryDepth = 1000
        };

        private static MqttBrokerProfile RejectAllProfile() =>
            BrokerProfileTestData.Create("Test", 3, 1_000_000, publishFailureRate: 1);

        private static (NetworkSimulator Graph, MqttClient Publisher, MqttClient Subscriber, IMqttBroker Broker)
            Build(MqttBrokerProfile? profile, int randomSeed = 0)
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
                ticks, true, profile, randomSeed);
            network.AddServer(broker);
            graph.AddServer(broker);

            var publisher = new MqttClient(mqttPacketManager, 1, "pub", "mqtt://pub.net1.local",
                "mqtt", "mqtt-client", "pub", ticks, true);
            network.AddClient(publisher);
            graph.AddClient(publisher);

            var subscriber = new MqttClient(mqttPacketManager, 2, "sub", "mqtt://sub.net1.local",
                "mqtt", "mqtt-client", "sub", ticks, true);
            network.AddClient(subscriber);
            graph.AddClient(subscriber);

            return (graph, publisher, subscriber, broker);
        }

        private static void RunUntil(NetworkSimulator graph, Func<bool> condition, int maxRefresh = 10_000)
        {
            for (var i = 0; i < maxRefresh && !condition(); i++)
            {
                graph.Refresh(false, 0);
            }
        }

        [TestMethod]
        public void BrokerWithoutProfile_DeliversMessage()
        {
            var (graph, publisher, subscriber, _) = Build(profile: null);

            publisher.BeginConnect("mqs1");
            subscriber.BeginConnect("mqs1");

            RunUntil(graph, () => publisher.IsConnected && subscriber.IsConnected);

            Assert.IsTrue(subscriber.BeginSubscribe("test/+", MqttQos.AtLeastOnce));
            RunUntil(graph, () => subscriber.IsSubscribed("test/+"));
            Assert.IsTrue(subscriber.IsSubscribed("test/+"), "SUBACK must be received before publishing");

            var received = 0;
            subscriber.MessageReceived += (_, _) => received++;

            publisher.Publish("test/data", new byte[] { 1, 2, 3 }, MqttQos.AtLeastOnce);

            RunUntil(graph, () => received > 0);

            Assert.AreEqual(1, received);
        }

        [TestMethod]
        public void BrokerWithRejectAllProfile_DoesNotDeliverMessage()
        {
            var (graph, publisher, subscriber, _) = Build(RejectAllProfile());

            publisher.BeginConnect("mqs1");
            subscriber.BeginConnect("mqs1");

            RunUntil(graph, () => publisher.IsConnected && subscriber.IsConnected);

            Assert.IsTrue(subscriber.BeginSubscribe("test/+", MqttQos.AtLeastOnce));
            RunUntil(graph, () => subscriber.IsSubscribed("test/+"));
            Assert.IsTrue(subscriber.IsSubscribed("test/+"), "SUBACK must be received before publishing");

            var received = 0;
            subscriber.MessageReceived += (_, _) => received++;

            publisher.Publish("test/data", new byte[] { 1, 2, 3 }, MqttQos.AtLeastOnce);

            // Даём сообщению пройти через сети; при refuse-профиле доставки не будет.
            for (var i = 0; i < 10_000; i++)
            {
                graph.Refresh(false, 0);
            }

            Assert.AreEqual(0, received);
        }

        [TestMethod]
        public void BrokerWithoutProfile_Qos0FansOutWithoutAcknowledgement()
        {
            var (graph, publisher, subscriber, _) = Build(profile: null);
            Assert.IsTrue(publisher.BeginConnect("mqs1"));
            Assert.IsTrue(subscriber.BeginConnect("mqs1"));
            RunUntil(graph, () => publisher.IsConnected && subscriber.IsConnected);
            Assert.IsTrue(subscriber.BeginSubscribe("test/+", MqttQos.AtMostOnce));
            RunUntil(graph, () => subscriber.IsSubscribed("test/+"));

            var received = 0;
            subscriber.MessageReceived += (_, message) =>
            {
                Assert.AreEqual(MqttQos.AtMostOnce, message.Qos);
                received++;
            };

            Assert.IsTrue(publisher.Publish("test/data", new byte[] { 1, 2, 3 }, MqttQos.AtMostOnce));
            RunUntil(graph, () => received > 0);

            Assert.AreEqual(1, received);
        }

        [TestMethod]
        public void DeliveryLoss_IsCountedAfterSuccessfulQos1Handshake()
        {
            var profile = BrokerProfileTestData.Create(
                "DropDelivery", 3, 1_000_000, deliveryLossRate: 1, latencyMs: 0.1);
            var (graph, publisher, subscriber, broker) = Build(profile);
            Assert.IsTrue(publisher.BeginConnect("mqs1"));
            Assert.IsTrue(subscriber.BeginConnect("mqs1"));
            RunUntil(graph, () => publisher.IsConnected && subscriber.IsConnected);
            Assert.IsTrue(subscriber.BeginSubscribe("test/+", MqttQos.AtLeastOnce));
            RunUntil(graph, () => subscriber.IsSubscribed("test/+"));
            graph.ResetMeasurement();

            var received = 0;
            subscriber.MessageReceived += (_, _) => received++;
            Assert.IsTrue(publisher.Publish("test/data", new byte[] { 1, 2, 3 }, MqttQos.AtLeastOnce));
            RunUntil(graph, () => broker.GetMetrics().ByQos[MqttQos.AtLeastOnce].Completed == 1);

            var metrics = broker.GetMetrics().ByQos[MqttQos.AtLeastOnce];
            Assert.AreEqual(1L, metrics.Attempted);
            Assert.AreEqual(1L, metrics.Admitted);
            Assert.AreEqual(1L, metrics.Completed,
                "PUBACK must complete independently of subscriber delivery");
            Assert.AreEqual(1L, metrics.ExpectedDeliveries);
            Assert.AreEqual(0L, metrics.Delivered);
            Assert.AreEqual(1L, metrics.DeliveryDropped);
            Assert.AreEqual(0, received);
        }

        [TestMethod]
        public void PublishFailure_UsesExplicitSeedAndLocalPublishSequence()
        {
            const int seed = 73;
            const int attempts = 25;
            var profile = BrokerProfileTestData.Create(
                "SeededFailure", 3, 1_000_000, publishFailureRate: 0.5, latencyMs: 0.1);
            var (graph, publisher, _, broker) = Build(profile, seed);
            Assert.IsTrue(publisher.BeginConnect("mqs1"));
            RunUntil(graph, () => publisher.IsConnected);
            graph.ResetMeasurement();

            for (var sequence = 1; sequence <= attempts; sequence++)
            {
                Assert.IsTrue(publisher.Publish(
                    "test/data", new byte[] { 1, 2, 3 }, MqttQos.AtLeastOnce));
                for (var tick = 0; tick < 20; tick++) graph.Refresh(false, 0);
            }

            var expectedFailures = Enumerable.Range(1, attempts).LongCount(sequence =>
                DeterministicMqttSampler.Uniform(
                    seed, "mqs1", "pub", sequence, "publish-failure") < 0.5);
            var metrics = broker.GetMetrics().ByQos[MqttQos.AtLeastOnce];
            Assert.AreEqual(attempts, metrics.Attempted);
            Assert.AreEqual(expectedFailures, metrics.PublishFailed);
            Assert.AreEqual(0L, metrics.RateRejected);
            Assert.AreEqual(attempts - expectedFailures, metrics.Admitted);
            RunUntil(graph, () => graph.IsQuiescent);
            Assert.IsTrue(graph.IsQuiescent, "Profile failures must not leave an orphaned MQTT handshake");
        }
    }
}
