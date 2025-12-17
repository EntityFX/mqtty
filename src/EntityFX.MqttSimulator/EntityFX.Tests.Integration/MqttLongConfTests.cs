using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Plugin.Mqtt.Helper;
using EntityFX.MqttY.Utils;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Counter;
using EntityFX.MqttY.Plugin.Mqtt.Counter;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;
using System.Xml.Linq;
using static EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt.MqttRelayConfiguration;
using EntityFX.MqttY.Scenarios;
using EntityFX.MqttY.Plugin.Mqtt;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class MqttLongConfTests
    {
        private DijkstraWeightedIndexPathFinder pathFinder;
        private readonly ActionClientBuilder clientBuilder;
        private INetworkLogger? _monitoring;
        private TicksOptions tickOptions;
        private INetworkLoggerProvider? _monitoringProvider;
        private NetworkSimulator? _graph;
        private NetworkOptions _networkOptions;
        private Exception? _testException;
        private StringBuilder _logSb;

        private bool IsParallelRefresh = true;
        private MqttTopicEvaluator mqttTopicEvaluator;
        private MqttNativePacketManager mqttPacketManager;

        public MqttLongConfTests()
        {
            //var pathFinder = new DijkstraPathFinder();
            //var pathFinder2 = new DijkstraIndexPathFinder();
            pathFinder = new DijkstraWeightedIndexPathFinder();


            clientBuilder = new ActionClientBuilder(AppBuild);

            _monitoring = new NullNetworkLogger();
            tickOptions = new TicksOptions()
            {
                OutgoingWaitTicks = 2,
                TickPeriod = TimeSpan.FromMilliseconds(1)
            };
            _graph = new NetworkSimulator(pathFinder, _monitoring, tickOptions, true);

            _networkOptions = new NetworkOptions()
            {
                NetworkType = "eth",
                TransferTicks = 2,
                Speed = 18750000
            };


            //_monitoringProvider = new NullNetworkLoggerProvider(_monitoring);
            _monitoring = new NetworkLogger(false, TimeSpan.FromMilliseconds(1), new MonitoringIgnoreOption() { Category = new string[] { "Refresh", "Link" } });
            _logSb = new StringBuilder();

            _monitoringProvider = new SimpleNetworkLoggerProvider(_monitoring, _logSb);
            _monitoringProvider.Start();

            mqttTopicEvaluator = new MqttTopicEvaluator(true);
            mqttPacketManager = new MqttNativePacketManager(mqttTopicEvaluator);
        }

        private IClient AppBuild(int index, string name, string protocolType, string specification, INetwork network, TicksOptions ticks,
            bool enableCounters,
            string? group, int? groupAmount, Dictionary<string, string[]>? additional)
        {
            var address = $"mqtt://{name}";
            var clientName = name.Replace(".", "");

            var mqttClient = new MqttClient(mqttPacketManager, index,
            name, address,
            protocolType, specification, clientName, ticks, enableCounters)
            {
                Group = group,
                GroupAmount = groupAmount
            };

            //options.Network, options.NetworkGraph,

            network!.AddClient(mqttClient);
            network.NetworkSimulator!.AddClient(mqttClient);

            return mqttClient;
        }

        [TestInitialize]
        public void Initialize()
        {


            var builder = new MqttNetworkBuilder(_graph!, mqttPacketManager, mqttTopicEvaluator, clientBuilder);

            _graph!.Construction = true;
            var networks = builder.BuildTree(5, 4, 10, 2, null, true, tickOptions, _networkOptions);
            //var networks = builder.BuildTree(3, 3, 3, 1, true, tickOptions);
            _graph.Construction = false;
            _graph.UpdateRoutes();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _graph!.Clear();
        }

        [TestMethod]
        public void SimpleCommunicationTest()
        {
            var mqc1426 = _graph!.GetNode("mqc1426.n1419.n1392.n1325.n0.net", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1426);

            var mqb782 = _graph.GetNode("mqb782.n770.n730.n663.n0.net", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqb782);

            List<List<int[]>> adj = new List<List<int[]>>();
            //for (int i = 0; i < V; i++)
            //    adj.Add(new List<int[]>());


            var path = _graph.PathFinder.GetPath(mqc1426.Network!, mqb782.Network!);

            var plantUmlGraphGenerator = new SimpleGraphMlGenerator();
            var uml = plantUmlGraphGenerator.SerializeNetworkGraph(_graph!);
        }

        [TestMethod]
        public void BuildRandomTest()
        {
            var graph = new NetworkSimulator(pathFinder, _monitoring!, tickOptions, true);
            var builder = new MqttNetworkBuilder(graph!, mqttPacketManager, mqttTopicEvaluator, clientBuilder);

            graph!.Construction = true;
            var networks = builder.BuildRandomNodesTree(3, 3, (2, 10), (1, 3), null, true, tickOptions, _networkOptions);
            //var networks = builder.BuildTree(3, 3, 3, 1, true, tickOptions);
            graph.Construction = false;
            graph.UpdateRoutes();

            var plantUmlGraphGenerator = new SimpleGraphMlGenerator();
            var uml = plantUmlGraphGenerator.SerializeNetworkGraph(graph!);
        }

        [TestMethod]
        public void BuildChainTest()
        {
            var graph = new NetworkSimulator(pathFinder, _monitoring!, tickOptions, true);
            var builder = new MqttNetworkBuilder(graph!, mqttPacketManager, mqttTopicEvaluator, clientBuilder);

            graph!.Construction = true;
            var networks = builder.BuildChain(3, 10, 5, 2, null, true, tickOptions, _networkOptions);
            //var networks = builder.BuildTree(3, 3, 3, 1, true, tickOptions);
            graph.Construction = false;
            graph.UpdateRoutes();

            var plantUmlGraphGenerator = new SimpleGraphMlGenerator();
            var uml = plantUmlGraphGenerator.SerializeNetworkGraph(graph!);
        }

        [TestMethod]
        public void BuildSimpleTreeTest()
        {
            var graph = new NetworkSimulator(pathFinder, _monitoring!, tickOptions, true);
            var builder = new MqttNetworkBuilder(graph!, mqttPacketManager, mqttTopicEvaluator, clientBuilder);

            graph!.Construction = true;
            var networks = builder.BuildSimpleTree(3, 10, 2, 1, null, true, tickOptions, _networkOptions);
            //var networks = builder.BuildTree(3, 3, 3, 1, true, tickOptions);
            graph.Construction = false;
            graph.UpdateRoutes();

            var plantUmlGraphGenerator = new SimpleGraphMlGenerator();
            var uml = plantUmlGraphGenerator.SerializeNetworkGraph(graph!);
        }

        [TestMethod]
        [DataRow(true, 5, 2, 3, 10)]
        [DataRow(false, 5, 2, 3, 10)]
        [DataRow(true, 5, 2, 15, 10)]
        [DataRow(false, 5, 2, 15, 10)]
        [DataRow(true, 5, 20, 3, 10)]
        [DataRow(false, 5, 20, 3, 10)]
        [DataRow(true, 15, 2, 3, 10)]
        [DataRow(false, 15, 2, 3, 10)]
        [DataRow(true, 5, 2, 3, 100)]
        [DataRow(false, 5, 2, 3, 100)]
        public void BuildRelayTreeTest(bool isParallel, int relays, int length, int clients, int sendRepeats)
        {
            var graph = new NetworkSimulator(pathFinder, _monitoring!, tickOptions, true);
            var builder = new MqttNetworkBuilder(graph!, mqttPacketManager, mqttTopicEvaluator, clientBuilder);


            graph!.Construction = true;
            var networks = builder.BuildSimpleTree(relays, length, clients, 1, null, true, tickOptions, _networkOptions);

            var brokers = builder.BuildMqttRelay(graph, tickOptions);

            graph.Construction = false;
            graph.UpdateRoutes();

            var mqttRelays = graph.Applications.Values.OfType<MqttRelay>();
            foreach (var mqttRelay in mqttRelays)
            {
                mqttRelay!.Start();
            }

            var mqttReceivers = graph.Applications.Values.OfType<MqttReceiver>();
            foreach (var mqttReceiver in mqttReceivers)
            {
                mqttReceiver!.Start();
            }


            foreach (var broker in brokers)
            {
                var brokerNetwork = broker.Network!;
                var mqttClients = brokerNetwork.Clients.Values.OfType<MqttClient>().ToArray();
                foreach (var mqttClient in mqttClients)
                {
                    mqttClient.BeginConnect(broker.Name);
                }
            }

            var allConnected = RefreshUntilConnected(isParallel, graph);

            var ticks = graph.TotalTicks;
            foreach (var mqttRelay in mqttRelays)
            {
                mqttRelay.SubscribeAll();
            }

            RefreshTicks(isParallel, graph, ticks);

            foreach (var mqttReceiver in mqttReceivers)
            {
                mqttReceiver.SubscribeAll();
            }

            RefreshTicks(isParallel, graph, ticks);

            //Console.WriteLine(graph.Counters.PrintCounters());

            //Console.WriteLine(_logSb.ToString());

            var plantUmlGraphGenerator = new SimpleGraphMlGenerator();
            var uml = plantUmlGraphGenerator.SerializeNetworkGraph(graph!);


            ///TEST Relay subscriptions
            var countRelaySubscribed = 0;
            foreach (var mqttRelay in mqttRelays)
            {
                var options = mqttRelay.Options!;

                foreach (var listenTopic in options.ListenTopics)
                {
                    foreach (var topic in listenTopic.Value.Topics)
                    {
                        var hasSubscribtion = mqttRelay.HasListenSubscription(listenTopic.Key, listenTopic.Value.Server, topic);

                        if (!hasSubscribtion)
                        {
                            Assert.Fail($"Missing subsciption {listenTopic.Key} for server {listenTopic.Value.Server} and topic {topic}");
                        }

                        countRelaySubscribed++;
                    }
                }


            }

            ///Test Receiver subsciptions
            var countReceiverSubscribed = 0;
            foreach (var mqttReceiver in mqttReceivers)
            {
                var options = mqttReceiver.Options!;

                foreach (var listenTopic in options.Topics)
                {

                    var hasSubscribtion = mqttReceiver.HasListenSubscription(options.Server, listenTopic);

                    if (!hasSubscribtion)
                    {
                        Assert.Fail($"Missing subsciption for server {options.Server} and topic {listenTopic}");
                    }

                    countReceiverSubscribed++;
                }

            }


            var data = new { Temperature = 25.0, Hummidity = 50.0, Pressure = 720.0 };
            var dataJson = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(dataJson);

            for (int r = 0; r < 100; r++)
            {
                foreach (var broker in brokers)
                {
                    var brokerNetwork = broker.Network!;
                    var mqttClients = brokerNetwork.Clients.Values.OfType<MqttClient>().Where(c => c.Group == null);

                    foreach (var mqttClient in mqttClients)
                    {
                        mqttClient.Publish("telemetry/data", bytes, MqttQos.AtLeastOnce, false);
                    }
                }

                RefreshTicks(isParallel, graph, ticks);
                RefreshTicks(isParallel, graph, ticks);


            }

            //var receivedByAll = mqttReceivers.Sum(r => r.Received);

            Console.WriteLine(graph.Counters.PrintCounters());
            //foreach (var client in graph.Clients.Values)
            //{
            //    var clientMqttCounters = GetMqttCounters(client);
            //}
        }

        private static bool RefreshUntilConnected(bool isParallel, NetworkSimulator graph)
        {
            var allConnected = false;

            while (!allConnected)
            {
                allConnected = graph!.Clients.All(c => c.Value.IsConnected);
                //var nonConnected = _graph!.Clients.Where(c => !c.Value.IsConnected);
                graph.RefreshWithCounters(isParallel);
            }

            return allConnected;
        }

        private void RefreshTicks(bool isParallel, NetworkSimulator graph, long ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                graph.RefreshWithCounters(isParallel);
            }
        }

        [TestMethod]
        public void MqttConnectTest()
        {
            _graph!.Counters.Clear();

            var mqc1426 = _graph!.GetNode("mqc1426.n1419.n1392.n1325.n0.net", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1426);

            var mqb782 = _graph.GetNode("mqb782.n770.n730.n663.n0.net", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqb782);

            mqc1426.BeginConnect("mqb782.n770.n730.n663.n0.net", false);

            for (int i = 0; i < 500; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
                //_graph.Refresh(IsParallelRefresh);
            }

            Assert.IsTrue(mqc1426.IsConnected);

            var mqc1426C = GetMqttCounters(mqc1426);
            var mqb782S = GetMqttCounters(mqb782);


            Assert.AreEqual(mqc1426C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(mqb782S!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(mqc1426C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            Assert.AreEqual(mqb782S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);

            Console.WriteLine(_graph.Counters.PrintCounters());
        }

        private static MqttCounters? GetMqttCounters(INode node)
        {
            return (node.Counters as NodeCounters)?.Counters.OfType<MqttCounters>().FirstOrDefault();
        }

    }

}