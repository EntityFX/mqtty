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
            _graph = new NetworkSimulator(pathFinder, _monitoring, tickOptions);

            _networkOptions = new NetworkOptions()
            {
                NetworkType = "eth",
                TransferTicks = 2,
                Speed = 18750000
            };


            _monitoringProvider = new NullNetworkLoggerProvider(_monitoring);
            _monitoringProvider.Start();

            mqttTopicEvaluator = new MqttTopicEvaluator(true);
            mqttPacketManager = new MqttNativePacketManager(mqttTopicEvaluator);
        }

        private IClient AppBuild((int index, string name, string protocolType, string specification, INetwork network, TicksOptions ticks, string? group, int? groupAmount, Dictionary<string, string[]>? additional) tuple)
        {
            throw new NotImplementedException();
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
            var graph = new NetworkSimulator(pathFinder, _monitoring!, tickOptions);
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
            var graph = new NetworkSimulator(pathFinder, _monitoring!, tickOptions);
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
            var graph = new NetworkSimulator(pathFinder, _monitoring!, tickOptions);
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
        public void BuildRelayTreeTest()
        {
            var graph = new NetworkSimulator(pathFinder, _monitoring!, tickOptions);
            var builder = new MqttNetworkBuilder(graph!, mqttPacketManager, mqttTopicEvaluator, clientBuilder);

            var appuildOptions = new Dictionary<string, (int Index, NetworkBuilderApplicationFunc<object>? AppOptionsFunc)>()
            {
                ["mqtt-receiver"] = (1, (index, name, specification) => new MqttReceiverConfiguration()
                {
                    Topics = new string[] {
                            "telemetry/+",
                            "local/telemetry/+"
                        }
                })
            };

            graph!.Construction = true;
            var networks = builder.BuildSimpleTree(3, 2, 2, 1, appuildOptions, true, tickOptions, _networkOptions);
            var brokers = graph.Servers.Values.OfType<IMqttBroker>().ToArray();

            var ix = 3 * 2 * 2;
            foreach (var broker in brokers!)
            {
                var oppositeBrokers = brokers.Where(b => b.Name != broker.Name);

                var brokerNetwork = broker.Network;
                var relayName = $"mqrl{ix}.{brokerNetwork!.Name}";
                var address = $"mqtt://{relayName}";

                var relayRemoteTopics = oppositeBrokers.ToDictionary(k => $"rs{k.Index}",
                    v => new MqttRelayConfigurationItem() { ReplaceRelaySegment = false, Server = v.Name, TopicPrefix = $"relay{v.Index}" });

                var relay = new MqttRelay(ix, relayName, address, "mqtt", "mqtt-relay", clientBuilder, mqttTopicEvaluator, tickOptions,
                    new MqttRelayConfiguration()
                    {
                        ListenTopics = new Dictionary<string, MqttRelayConfiguration.MqttListenConfigurationItem>()
                        {
                            ["ls1"] = new MqttRelayConfiguration.MqttListenConfigurationItem()
                            {
                                Server = broker.Name,
                                Topics = new string[] { "telemetry/+" }
                            },
                            ["rls1"] = new MqttRelayConfiguration.MqttListenConfigurationItem()
                            {
                                Server = broker.Name,
                                Topics = new string[] { $"relay{ix}/telemetry/+" }
                            },
                        },
                        RelayTopics = relayRemoteTopics
                    });
                brokerNetwork.AddApplication(relay);
                graph.AddApplication(relay);
                ix++;
            }

            graph.Construction = false;
            graph.UpdateRoutes();

            var plantUmlGraphGenerator = new SimpleGraphMlGenerator();
            var uml = plantUmlGraphGenerator.SerializeNetworkGraph(graph!);
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