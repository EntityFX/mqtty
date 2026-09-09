using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    [TestCategory(TestCategories.Protocol)]
    public class BrokerQos2Tests
    {
        private static TicksOptions Ticks => new()
        {
            TickPeriod = TimeSpan.FromMilliseconds(0.1),
            OutgoingWaitTicks = 1,
            ReceiveWaitPeriod = TimeSpan.FromSeconds(30),
            CounterHistoryDepth = 1000
        };

        private sealed class InspectableMqttBroker : MqttBroker
        {
            public InspectableMqttBroker(IMqttPacketManager packetManager, IMqttTopicEvaluator topicEvaluator,
                TicksOptions ticks) : base(packetManager, topicEvaluator, 0, "mqs1",
                "mqtt://mqs1.net1.local", "mqtt", "mqtt-server", ticks, true) { }

            public void Inject(INetworkPacket packet) => base.OnReceived(packet);
        }

        private sealed class InspectableMqttClient : MqttClient
        {
            public InspectableMqttClient(IMqttPacketManager packetManager, int index, string name,
                TicksOptions ticks) : base(packetManager, index, name, $"mqtt://{name}.net1.local",
                "mqtt", "mqtt-client", name, ticks, true) { }

            public void Inject(INetworkPacket packet) => base.OnReceived(packet);
        }

        private static (NetworkSimulator Graph, InspectableMqttBroker Broker, InspectableMqttClient Publisher,
            InspectableMqttClient Subscriber, IMqttPacketManager PacketManager) Build()
        {
            var ticks = Ticks;
            var pathFinder = new DijkstraPathFinder();
            var monitoring = new NetworkLogger(false, TimeSpan.FromMilliseconds(0.1),
                new MonitoringIgnoreOption { Category = new[] { "Refresh", "Link" } });

            var graph = new NetworkSimulator(pathFinder, monitoring, ticks, true);
            graph.OnError += (_, ex) => Console.WriteLine($"SIM ERROR: {ex}");

            var mqttTopicEvaluator = new MqttTopicEvaluator(true);
            var mqttPacketManager = new MqttNativePacketManager(mqttTopicEvaluator);

            var network = new Network(0, "net1", "net1.local", "eth",
                new NetworkOptions { NetworkType = "eth", TransferTicks = 1, Speed = 18750000 },
                ticks, true);
            graph.AddNetwork(network);

            var broker = new InspectableMqttBroker(mqttPacketManager, mqttTopicEvaluator, ticks);
            network.AddServer(broker);
            graph.AddServer(broker);

            var publisher = new InspectableMqttClient(mqttPacketManager, 1, "pub", ticks);
            network.AddClient(publisher);
            graph.AddClient(publisher);

            var subscriber = new InspectableMqttClient(mqttPacketManager, 2, "sub", ticks);
            network.AddClient(subscriber);
            graph.AddClient(subscriber);

            return (graph, broker, publisher, subscriber, mqttPacketManager);
        }

        private static void RunRefresh(NetworkSimulator graph, int iterations)
        {
            for (var i = 0; i < iterations; i++)
            {
                graph.Refresh(false, 0);
            }
        }

        [TestMethod]
        public void Qos2_SubscriberWithQos2_ReceivesMessage()
        {
            var (graph, _, publisher, subscriber, _) = Build();

            publisher.BeginConnect("mqs1");
            subscriber.BeginConnect("mqs1");

            // Прогоняем до установления соединений.
            for (var i = 0; i < 100_000 && !(publisher.IsConnected && subscriber.IsConnected); i++)
            {
                graph.Refresh(false, 0);
            }

            Assert.IsTrue(subscriber.BeginSubscribe("test/+", MqttQos.ExactlyOnce));
            for (var i = 0; i < 100_000 && !subscriber.IsSubscribed("test/+"); i++)
            {
                graph.Refresh(false, 0);
            }
            Assert.IsTrue(subscriber.IsSubscribed("test/+"), "SUBACK must be received before publishing");

            var received = 0;
            subscriber.MessageReceived += (_, _) => received++;

            publisher.Publish("test/data", new byte[] { 1, 2, 3 }, MqttQos.ExactlyOnce);

            // Ожидаем доставку с ограничением по числу рефрешей (устраняет флаканесс).
            for (var i = 0; i < 100_000 && received == 0; i++)
            {
                graph.Refresh(false, 0);
            }

            Assert.AreEqual(1, received, "QoS2 subscriber should receive exactly one copy");
            for (var i = 0; i < 100_000 && !graph.IsQuiescent; i++) graph.Refresh(false, 0);
            Assert.IsTrue(graph.IsQuiescent, "QoS2 handshake must drain completely");
        }

        [TestMethod]
        public void PublisherQos2_FansOutOnFirstPubRelOnly()
        {
            var (graph, broker, publisher, subscriber, packetManager) = Build();
            Assert.IsTrue(publisher.BeginConnect("mqs1"));
            Assert.IsTrue(subscriber.BeginConnect("mqs1"));
            RunRefresh(graph, 100);
            Assert.IsTrue(publisher.IsConnected && subscriber.IsConnected);
            Assert.AreEqual(SessionState.CleanSession, subscriber.CurrentSessionState);
            Assert.IsTrue(subscriber.BeginSubscribe("test/+", MqttQos.ExactlyOnce));
            for (var i = 0; i < 100 && !subscriber.IsSubscribed("test/+"); i++) graph.Refresh(false, 0);
            Assert.IsTrue(subscriber.IsSubscribed("test/+"));

            var received = 0;
            subscriber.MessageReceived += (_, _) => received++;
            var publish = new PublishPacket("test/data", MqttQos.ExactlyOnce, false, false, 77)
            {
                Payload = new byte[] { 1, 2, 3 }
            };
            var publishNetworkPacket = new NetworkPacket<int>(graph.GetPacketId(), null, 0,
                publisher.Name, broker.Name, NodeType.Client, NodeType.Server, publisher.Index, broker.Index,
                packetManager.PacketToBytes(publish), "mqtt", 0, 1, Category: "MQTT Publish");

            broker.Inject(publishNetworkPacket);
            broker.Inject(publishNetworkPacket);
            RunRefresh(graph, 100);
            Assert.AreEqual(0, received, "QoS2 PUBLISH must not fan out before PUBREL");
            Assert.IsFalse(graph.IsQuiescent, "stored QoS2 handshake is pending work");

            var release = new PublishReleasePacket(77);
            var releaseNetworkPacket = new NetworkPacket<int>(graph.GetPacketId(), null, 0,
                publisher.Name, broker.Name, NodeType.Client, NodeType.Server, publisher.Index, broker.Index,
                packetManager.PacketToBytes(release), "mqtt", 0, 1, Category: "MQTT PubRel");
            broker.Inject(releaseNetworkPacket);
            broker.Inject(releaseNetworkPacket);
            for (var i = 0; i < 1000 && received == 0; i++) graph.Refresh(false, 0);
            RunRefresh(graph, 100);

            Assert.AreEqual(1, received, "duplicate PUBREL must not repeat fan-out");
            Assert.IsTrue(graph.IsQuiescent, "completed QoS2 exchange must drain");
        }

        [TestMethod]
        public void SubscriberQos2_RaisesMessageOnFirstPubRelOnly()
        {
            var (graph, broker, _, subscriber, packetManager) = Build();
            Assert.IsTrue(subscriber.BeginConnect("mqs1"));
            for (var i = 0; i < 100 && !subscriber.IsConnected; i++) graph.Refresh(false, 0);
            Assert.IsTrue(subscriber.IsConnected);

            var received = 0;
            subscriber.MessageReceived += (_, _) => received++;
            var publish = new PublishPacket("test/data", MqttQos.ExactlyOnce, false, false, 88)
            {
                Payload = new byte[] { 4, 5, 6 }
            };
            var publishNetworkPacket = new NetworkPacket<int>(graph.GetPacketId(), null, 0,
                broker.Name, subscriber.Name, NodeType.Server, NodeType.Client, broker.Index, subscriber.Index,
                packetManager.PacketToBytes(publish), "mqtt", 0, 1, Category: "MQTT Publish");

            subscriber.Inject(publishNetworkPacket);
            subscriber.Inject(publishNetworkPacket);
            Assert.AreEqual(0, received, "subscriber must wait for PUBREL");

            var release = new PublishReleasePacket(88);
            var releaseNetworkPacket = new NetworkPacket<int>(graph.GetPacketId(), null, 0,
                broker.Name, subscriber.Name, NodeType.Server, NodeType.Client, broker.Index, subscriber.Index,
                packetManager.PacketToBytes(release), "mqtt", 0, 1, Category: "MQTT PubRel");
            subscriber.Inject(releaseNetworkPacket);
            subscriber.Inject(releaseNetworkPacket);

            Assert.AreEqual(1, received, "duplicate PUBREL must only resend PUBCOMP");
        }

        [TestMethod]
        public void Reconnect_PersistentSessionRestoresSubscription_AndCleanSessionClearsIt()
        {
            var (graph, _, publisher, subscriber, _) = Build();
            Assert.IsTrue(publisher.BeginConnect("mqs1", cleanSession: false));
            Assert.IsTrue(subscriber.BeginConnect("mqs1", cleanSession: false));
            for (var i = 0; i < 1000 && !(publisher.IsConnected && subscriber.IsConnected); i++)
                graph.Refresh(false, 0);
            Assert.IsTrue(publisher.IsConnected && subscriber.IsConnected);

            Assert.IsTrue(subscriber.BeginSubscribe("persistent/#", MqttQos.AtLeastOnce));
            for (var i = 0; i < 1000 && !subscriber.IsSubscribed("persistent/#"); i++)
                graph.Refresh(false, 0);
            Assert.IsTrue(subscriber.IsSubscribed("persistent/#"));

            var received = 0;
            subscriber.MessageReceived += (_, _) => received++;

            Assert.IsTrue(subscriber.Disconnect());
            Assert.IsTrue(subscriber.BeginConnect("mqs1", cleanSession: false));
            for (var i = 0; i < 1000 && !subscriber.IsConnected; i++) graph.Refresh(false, 0);
            Assert.IsTrue(subscriber.IsConnected);
            Assert.AreEqual(SessionState.SessionPresent, subscriber.CurrentSessionState);
            Assert.IsTrue(subscriber.IsSubscribed("persistent/#"));

            Assert.IsTrue(publisher.Publish("persistent/data", new byte[] { 1 }, MqttQos.AtLeastOnce));
            for (var i = 0; i < 1000 && received == 0; i++) graph.Refresh(false, 0);
            Assert.AreEqual(1, received, "persistent broker session must retain its subscription");

            Assert.IsTrue(subscriber.Disconnect());
            Assert.IsTrue(subscriber.BeginConnect("mqs1", cleanSession: true));
            for (var i = 0; i < 1000 && !subscriber.IsConnected; i++) graph.Refresh(false, 0);
            Assert.IsTrue(subscriber.IsConnected);
            Assert.AreEqual(SessionState.CleanSession, subscriber.CurrentSessionState);
            Assert.IsFalse(subscriber.IsSubscribed("persistent/#"));

            Assert.IsTrue(publisher.Publish("persistent/data", new byte[] { 2 }, MqttQos.AtLeastOnce));
            RunRefresh(graph, 1000);
            Assert.AreEqual(1, received, "clean session must remove the broker subscription");

            Assert.IsTrue(subscriber.Disconnect());
            Assert.IsTrue(subscriber.BeginConnect("mqs1", cleanSession: false));
            for (var i = 0; i < 1000 && !subscriber.IsConnected; i++) graph.Refresh(false, 0);
            Assert.AreEqual(SessionState.CleanSession, subscriber.CurrentSessionState,
                "a previous clean session must not survive disconnect");
        }

        [TestMethod]
        public void Subscribe_WithoutConnectionFailsClearly()
        {
            var (_, _, _, subscriber, _) = Build();

            Assert.ThrowsException<MqttClientException>(() =>
                subscriber.Subscribe("test/#", MqttQos.AtLeastOnce));
        }

        [TestMethod]
        public void ReversePacket_SwapsSourceAndDestinationIndexes()
        {
            var (graph, _, publisher, _, _) = Build();
            var original = new NetworkPacket<int>(graph.GetPacketId(), null, 0,
                publisher.Name, "mqs1", NodeType.Client, NodeType.Server, 7, 11,
                new byte[] { 1 }, "mqtt", 0, 1);

            var reverse = graph.GetReversePacket(original, new byte[] { 2 }, "response");

            Assert.AreEqual(7, reverse.ToIndex);
            Assert.AreEqual(11, reverse.FromIndex);
        }
    }
}
