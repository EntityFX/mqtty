using EntityFX.MqttY;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Helper;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;

namespace EntityFX.Tests.Integration
{
    /// <summary>
    /// Проверяет базовые гипотезы архитектуры Mqtt Relay (глава 3):
    /// построение топологии с несколькими брокерами и доставку сообщений
    /// между областями через приложение-ретранслятор вручную.
    /// </summary>
    [TestClass]
    public class MqttRelayHypothesisTests
    {
        private DijkstraWeightedIndexPathFinder _pathFinder = null!;
        private INetworkLogger _monitoring = null!;
        private TicksOptions _ticks = null!;
        private NetworkOptions _networkOptions = null!;
        private MqttTopicEvaluator _topicEvaluator = null!;
        private MqttNativePacketManager _packetManager = null!;

        [TestInitialize]
        public void Initialize()
        {
            _pathFinder = new DijkstraWeightedIndexPathFinder();
            _monitoring = new NetworkLogger(false, TimeSpan.FromMilliseconds(0.1),
                new MonitoringIgnoreOption { Category = new[] { "Refresh", "Link" } });
            _ticks = new TicksOptions
            {
                TickPeriod = TimeSpan.FromMilliseconds(0.1),
                OutgoingWaitTicks = 2,
                ReceiveWaitPeriod = TimeSpan.FromSeconds(30),
                CounterHistoryDepth = 1000
            };
            _networkOptions = new NetworkOptions
            {
                NetworkType = "eth",
                TransferTicks = 2,
                Speed = 18750000
            };
            _topicEvaluator = new MqttTopicEvaluator(true);
            _packetManager = new MqttNativePacketManager(_topicEvaluator);
        }

        private static bool IsConnected(NetworkSimulator graph) =>
            graph.Clients.Values.All(c => c.IsConnected);

        private static void RunUntil(NetworkSimulator graph, Func<bool> condition, int maxRefresh = 100_000)
        {
            for (var i = 0; i < maxRefresh && !condition(); i++)
            {
                graph.Refresh(false, 0);
            }
        }

        [TestMethod]
        public void RelayTopology_BuildsThreeBrokers_AndConnectsClients()
        {
            var graph = new NetworkSimulator(_pathFinder, _monitoring, _ticks, true);
            var builder = new MqttNetworkBuilder(graph, _packetManager, _topicEvaluator,
                new ActionClientBuilder((ix, name, protocolType, specification, network, ticks, enableCounters, group, groupAmount, additional) =>
                {
                    var address = $"mqtt://{name}";
                    var client = new EntityFX.MqttY.Plugin.Mqtt.MqttClient(_packetManager, ix, name, address,
                        protocolType, specification, name, ticks, enableCounters)
                    {
                        Group = group,
                        GroupAmount = groupAmount
                    };
                    network.AddClient(client);
                    graph.AddClient(client);
                    return client;
                }));

            graph.Construction = true;
            builder.BuildTree(3, 2, 1, 1, null, true, _ticks, _networkOptions);
            graph.Construction = false;
            graph.UpdateRoutes();

            // Подключаем каждого клиента к «своему» серверу (клиент и сервер в одной ветке).
            foreach (var network in graph.Networks.Values)
            {
                foreach (var client in network.Clients.Values)
                {
                    var server = network.Servers.Values.FirstOrDefault();
                    if (server != null)
                    {
                        ((EntityFX.MqttY.Plugin.Mqtt.MqttClient)client).BeginConnect(server.Name);
                    }
                }
            }

            RunUntil(graph, () => IsConnected(graph));

            Assert.IsTrue(IsConnected(graph), "All clients should be connected to their local broker");
            Assert.AreEqual(3, graph.Servers.Count, "Expected 3 brokers");
        }

        [Ignore("Тяжёлый сценарий доставки; соответствует Ignore-тесту MqttLongConfTests.BuildRelayTreeTest.")]
        [TestMethod]
        public void RelayTopology_DeliversTelemetry_BetweenAreas()
        {
            var graph = new NetworkSimulator(_pathFinder, _monitoring, _ticks, true);
            var builder = new MqttNetworkBuilder(graph, _packetManager, _topicEvaluator,
                new ActionClientBuilder((ix, name, protocolType, specification, network, ticks, enableCounters, group, groupAmount, additional) =>
                {
                    var client = new EntityFX.MqttY.Plugin.Mqtt.MqttClient(_packetManager, ix, name, $"mqtt://{name}",
                        protocolType, specification, name, ticks, enableCounters)
                    { Group = group, GroupAmount = groupAmount };
                    network.AddClient(client);
                    graph.AddClient(client);
                    return client;
                }));

            graph.Construction = true;
            builder.BuildSimpleTree(3, 2, 1, 1, null, true, _ticks, _networkOptions);
            var brokers = builder.BuildMqttRelay(graph, _ticks);
            graph.Construction = false;
            graph.UpdateRoutes();

            var relays = graph.Applications.Values.OfType<MqttRelay>().ToArray();
            var receivers = graph.Applications.Values.OfType<MqttReceiver>().ToArray();

            foreach (var relay in relays) relay.Start();
            foreach (var receiver in receivers) receiver.Start();

            foreach (var broker in brokers)
            {
                foreach (var client in broker.Network!.Clients.Values.OfType<MqttClient>().Where(c => c.Group == null))
                {
                    client.BeginConnect(broker.Name);
                }
            }

            RunUntil(graph, () => IsConnected(graph));
            Assert.IsTrue(IsConnected(graph), "All clients should connect");

            foreach (var relay in relays) relay.SubscribeAll();
            foreach (var receiver in receivers) receiver.SubscribeAll();

            var data = new byte[] { 1, 2, 3, 4, 5 };
            foreach (var broker in brokers)
            {
                foreach (var client in broker.Network!.Clients.Values.OfType<MqttClient>().Where(c => c.Group == null))
                {
                    client.Publish("telemetry/data", data, MqttQos.AtLeastOnce);
                }
            }

            RunUntil(graph, () => receivers.Any(r => r.Received > 0));

            var totalReceived = receivers.Sum(r => r.Received);
            Assert.IsTrue(totalReceived > 0,
                "Receivers must receive telemetry relayed between areas");
        }
    }
}
