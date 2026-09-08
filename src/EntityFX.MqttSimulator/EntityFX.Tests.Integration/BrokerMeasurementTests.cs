using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Counter;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Counter;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class BrokerMeasurementTests
    {
        private static readonly TicksOptions Ticks = new()
        {
            TickPeriod = TimeSpan.FromMilliseconds(0.1),
            OutgoingWaitTicks = 1,
            ReceiveWaitPeriod = TimeSpan.FromSeconds(30),
            CounterHistoryDepth = 1000
        };

        private static (NetworkSimulator Graph, IMqttBroker Broker, MqttClient Publisher,
            MqttClient[] Subscribers) Build(int subscriberCount)
        {
            var graph = new NetworkSimulator(new DijkstraPathFinder(),
                new NetworkLogger(false, Ticks.TickPeriod,
                    new MonitoringIgnoreOption { Category = new[] { "Refresh", "Link" } }),
                Ticks, true);
            var evaluator = new MqttTopicEvaluator(true);
            var packetManager = new MqttNativePacketManager(evaluator);
            var network = new Network(0, "net1", "net1.local", "eth",
                new NetworkOptions { NetworkType = "eth", TransferTicks = 1, Speed = 18_750_000 },
                Ticks, true);
            graph.AddNetwork(network);

            var broker = new MqttBroker(packetManager, evaluator, 0, "mqs1",
                "mqtt://mqs1.net1.local", "mqtt", "mqtt-server", Ticks, true);
            network.AddServer(broker);
            graph.AddServer(broker);

            var publisher = new MqttClient(packetManager, 1, "pub", "mqtt://pub.net1.local",
                "mqtt", "mqtt-client", "pub", Ticks, true);
            network.AddClient(publisher);
            graph.AddClient(publisher);

            var subscribers = Enumerable.Range(0, subscriberCount).Select(index =>
            {
                var name = $"sub{index}";
                var subscriber = new MqttClient(packetManager, index + 2, name,
                    $"mqtt://{name}.net1.local", "mqtt", "mqtt-client", name, Ticks, true);
                network.AddClient(subscriber);
                graph.AddClient(subscriber);
                return subscriber;
            }).ToArray();

            return (graph, broker, publisher, subscribers);
        }

        private static void RefreshUntil(NetworkSimulator graph, Func<bool> condition, int limit = 20_000)
        {
            for (var i = 0; i < limit && !condition(); i++) graph.Refresh(false, 0);
            Assert.IsTrue(condition(), "condition was not reached before the refresh limit");
        }

        private static void ConnectAndSubscribe(
            NetworkSimulator graph, MqttClient publisher, IEnumerable<MqttClient> subscribers, MqttQos qos)
        {
            Assert.IsTrue(publisher.BeginConnect("mqs1"));
            foreach (var subscriber in subscribers) Assert.IsTrue(subscriber.BeginConnect("mqs1"));
            RefreshUntil(graph, () => publisher.IsConnected && subscribers.All(item => item.IsConnected));
            foreach (var subscriber in subscribers)
                Assert.IsTrue(subscriber.BeginSubscribe("measure/#", qos));
            RefreshUntil(graph, () => subscribers.All(item => item.IsSubscribed("measure/#")));
        }

        [TestMethod]
        public void Qos0Snapshot_SeparatesDeliveryAndUsesExactMeasurementWindow()
        {
            var (graph, broker, publisher, subscribers) = Build(1);
            ConnectAndSubscribe(graph, publisher, subscribers, MqttQos.AtMostOnce);
            var delivered = 0;
            subscribers[0].MessageReceived += (_, _) => delivered++;

            graph.ResetMeasurement();
            var startTick = graph.TotalTicks;
            Assert.IsTrue(publisher.Publish("measure/value", new byte[] { 1 }, MqttQos.AtMostOnce));
            RefreshUntil(graph, () => delivered == 1);
            while (graph.TotalTicks - startTick < 1_000) graph.Refresh(false, 0);

            var metrics = broker.GetMetrics();
            var qos = metrics.ByQos[MqttQos.AtMostOnce];
            Assert.AreEqual(1L, qos.Attempted);
            Assert.AreEqual(1L, qos.Admitted);
            Assert.AreEqual(1L, qos.Completed);
            Assert.AreEqual(1L, qos.ExpectedDeliveries);
            Assert.AreEqual(1L, qos.Delivered);
            Assert.AreEqual(0L, qos.RateRejected);
            Assert.AreEqual(0L, qos.PublishFailed);
            Assert.AreEqual(0L, qos.DeliveryDropped);
            Assert.AreEqual(10.0, qos.Rps, 0.0001);
            Assert.IsNull(qos.LatencyP50Ms);
            Assert.IsNull(qos.LatencyP95Ms);
            Assert.IsNull(qos.LatencyP99Ms);
            var legacyCounters = ((NodeCounters)broker.Counters).Counters.OfType<MqttCounters>().Single();
            Assert.AreEqual(qos.Rps, legacyCounters.PublishRpsByQos[MqttQos.AtMostOnce].Value);
        }

        [DataTestMethod]
        [DataRow(MqttQos.AtLeastOnce)]
        [DataRow(MqttQos.ExactlyOnce)]
        public void AcknowledgedQos_CompletesAtPublisherAndRecordsLatency(MqttQos publishQos)
        {
            var (graph, broker, publisher, subscribers) = Build(1);
            ConnectAndSubscribe(graph, publisher, subscribers, publishQos);
            graph.ResetMeasurement();

            Assert.IsTrue(publisher.Publish("measure/value", new byte[] { 1 }, publishQos));
            RefreshUntil(graph, () => graph.IsQuiescent);

            var qos = broker.GetMetrics().ByQos[publishQos];
            Assert.AreEqual(1L, qos.Attempted);
            Assert.AreEqual(1L, qos.Admitted);
            Assert.AreEqual(1L, qos.Completed);
            Assert.AreEqual(1L, qos.Delivered);
            Assert.IsNotNull(qos.LatencyP50Ms);
            Assert.IsTrue(qos.LatencyP50Ms > 0);
            Assert.AreEqual(qos.LatencyP50Ms, qos.LatencyP95Ms);
            Assert.AreEqual(qos.LatencyP50Ms, qos.LatencyP99Ms);
            var legacyCounters = ((NodeCounters)broker.Counters).Counters.OfType<MqttCounters>().Single();
            Assert.AreEqual(qos.LatencyP99Ms!.Value, legacyCounters.LatencyP99.Value);
        }

        [TestMethod]
        public void ResetMeasurement_PreservesProtocolStateAndExcludesOlderGeneration()
        {
            var (graph, broker, publisher, subscribers) = Build(1);
            ConnectAndSubscribe(graph, publisher, subscribers, MqttQos.ExactlyOnce);
            graph.ResetMeasurement();
            Assert.IsTrue(publisher.Publish("measure/value", new byte[] { 1 }, MqttQos.ExactlyOnce));
            RefreshUntil(graph, () =>
                broker.GetMetrics().ByQos[MqttQos.ExactlyOnce].Attempted == 1);

            graph.ResetMeasurement();
            Assert.IsTrue(publisher.IsConnected);
            Assert.IsTrue(subscribers[0].IsSubscribed("measure/#"));
            RefreshUntil(graph, () => graph.IsQuiescent);

            var qos = broker.GetMetrics().ByQos[MqttQos.ExactlyOnce];
            Assert.AreEqual(0L, qos.Attempted);
            Assert.AreEqual(0L, qos.Admitted);
            Assert.AreEqual(0L, qos.Completed);
            Assert.AreEqual(0L, qos.Delivered);
        }

        [TestMethod]
        public void MultipleSubscribers_UseExpectedAndActualDeliveryCounts()
        {
            var (graph, broker, publisher, subscribers) = Build(2);
            ConnectAndSubscribe(graph, publisher, subscribers, MqttQos.AtLeastOnce);
            graph.ResetMeasurement();
            Assert.IsTrue(publisher.Publish("measure/value", new byte[] { 1 }, MqttQos.AtLeastOnce));
            RefreshUntil(graph, () => graph.IsQuiescent);

            var qos = broker.GetMetrics().ByQos[MqttQos.AtLeastOnce];
            Assert.AreEqual(2L, qos.ExpectedDeliveries);
            Assert.AreEqual(2L, qos.Delivered);
        }

        [TestMethod]
        public void LatencyHistogram_UsesNearestRankAndEmptyIsNull()
        {
            var histogram = new LatencyHistogram();
            Assert.IsNull(histogram.NearestRank(0.99));
            for (var tick = 1L; tick <= 100; tick++) histogram.Record(tick);

            Assert.AreEqual(50L, histogram.NearestRank(0.50));
            Assert.AreEqual(95L, histogram.NearestRank(0.95));
            Assert.AreEqual(99L, histogram.NearestRank(0.99));
        }

        [TestMethod]
        public void DeliveryDrop_IsSeparateFromPublishFailure()
        {
            var measurement = new BrokerMeasurement();
            measurement.Reset(10);
            var tracker = measurement.BeginAttempt("pub", MqttQos.AtLeastOnce, 42, 100, 11);
            measurement.Admit(tracker);
            measurement.ExpectDelivery(tracker, 200);
            measurement.DeliveryDropped(200);

            var qos = measurement.Snapshot(20, Ticks.TickPeriod).ByQos[MqttQos.AtLeastOnce];
            Assert.AreEqual(1L, qos.Attempted);
            Assert.AreEqual(1L, qos.Admitted);
            Assert.AreEqual(1L, qos.ExpectedDeliveries);
            Assert.AreEqual(0L, qos.Delivered);
            Assert.AreEqual(1L, qos.DeliveryDropped);
            Assert.AreEqual(0L, qos.PublishFailed);
            Assert.AreEqual(0L, qos.RateRejected);
        }

        [TestMethod]
        public void ZeroDurationSnapshot_HasZeroRps()
        {
            var measurement = new BrokerMeasurement();
            measurement.Reset(10);
            var tracker = measurement.BeginAttempt("pub", MqttQos.AtMostOnce, null, 100, 10);
            measurement.Admit(tracker);

            var qos = measurement.Snapshot(10, Ticks.TickPeriod).ByQos[MqttQos.AtMostOnce];
            Assert.AreEqual(1L, qos.Completed);
            Assert.AreEqual(0.0, qos.Rps);
        }

        [TestMethod]
        public void Snapshot_IsSafeDuringConcurrentUpdates()
        {
            var measurement = new BrokerMeasurement();
            measurement.Reset(0);

            Parallel.For(0, 1_000, index =>
            {
                var tracker = measurement.BeginAttempt(
                    $"pub{index}", MqttQos.AtLeastOnce, (ushort)(index + 1), index, index);
                measurement.Admit(tracker);
                measurement.CompletePublisher(
                    tracker.Publisher, tracker.Qos, tracker.PacketId!.Value, index + 1);
                _ = measurement.Snapshot(index + 2, Ticks.TickPeriod);
            });

            var qos = measurement.Snapshot(1_002, Ticks.TickPeriod).ByQos[MqttQos.AtLeastOnce];
            Assert.AreEqual(1_000L, qos.Attempted);
            Assert.AreEqual(1_000L, qos.Completed);
        }
    }
}
