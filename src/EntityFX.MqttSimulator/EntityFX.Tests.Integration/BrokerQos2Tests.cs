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
    public class BrokerQos2Tests
    {
        private static TicksOptions Ticks => new()
        {
            TickPeriod = TimeSpan.FromMilliseconds(0.1),
            OutgoingWaitTicks = 1,
            ReceiveWaitPeriod = TimeSpan.FromSeconds(30),
            CounterHistoryDepth = 1000
        };

        private static (NetworkSimulator Graph, MqttClient Publisher, MqttClient Subscriber) Build()
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

            var broker = new MqttBroker(mqttPacketManager, mqttTopicEvaluator,
                0, "mqs1", "mqtt://mqs1.net1.local", "mqtt", "mqtt-server",
                ticks, true);
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
            var (graph, publisher, subscriber) = Build();

            publisher.BeginConnect("mqs1");
            subscriber.BeginConnect("mqs1");

            // Прогоняем до установления соединений.
            for (var i = 0; i < 100_000 && !(publisher.IsConnected && subscriber.IsConnected); i++)
            {
                graph.Refresh(false, 0);
            }

            subscriber.Subscribe("test/+", MqttQos.ExactlyOnce);

            var received = 0;
            subscriber.MessageReceived += (_, _) => received++;

            publisher.Publish("test/data", new byte[] { 1, 2, 3 }, MqttQos.ExactlyOnce);

            // Ожидаем доставку с ограничением по числу рефрешей (устраняет флаканесс).
            for (var i = 0; i < 100_000 && received == 0; i++)
            {
                graph.Refresh(false, 0);
            }

            Assert.AreEqual(1, received, "QoS2 subscriber should receive exactly one copy");
        }
    }
}