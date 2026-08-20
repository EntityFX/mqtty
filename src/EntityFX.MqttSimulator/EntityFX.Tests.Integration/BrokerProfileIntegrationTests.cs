using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class BrokerProfileIntegrationTests
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
            BrokerType = "Test",
            Qos0 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 1.0, 0.0, 1.0) } },
            Qos1 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 1.0, 0.0, 1.0) } },
            Qos2 = new MqttQosProfile { Samples = new[] { new MqttQosSample(1, 1.0, 0.0, 1.0) } },
        };

        private static (NetworkSimulator Graph, MqttClient Publisher, MqttClient Subscriber)
            Build(MqttBrokerProfile? profile)
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

            var subscriber = new MqttClient(mqttPacketManager, 2, "sub", "mqtt://sub.net1.local",
                "mqtt", "mqtt-client", "sub", ticks, true);
            network.AddClient(subscriber);
            graph.AddClient(subscriber);

            return (graph, publisher, subscriber);
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
            var (graph, publisher, subscriber) = Build(profile: null);

            publisher.BeginConnect("mqs1");
            subscriber.BeginConnect("mqs1");

            RunUntil(graph, () => publisher.IsConnected && subscriber.IsConnected);

            subscriber.Subscribe("test/+", MqttQos.AtLeastOnce);

            var received = 0;
            subscriber.MessageReceived += (_, _) => received++;

            publisher.Publish("test/data", new byte[] { 1, 2, 3 }, MqttQos.AtLeastOnce);

            RunUntil(graph, () => received > 0);

            Assert.AreEqual(1, received);
        }

        [TestMethod]
        public void BrokerWithRejectAllProfile_DoesNotDeliverMessage()
        {
            var (graph, publisher, subscriber) = Build(RejectAllProfile());

            publisher.BeginConnect("mqs1");
            subscriber.BeginConnect("mqs1");

            RunUntil(graph, () => publisher.IsConnected && subscriber.IsConnected);

            subscriber.Subscribe("test/+", MqttQos.AtLeastOnce);

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
    }
}