using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Plugin.Mqtt.Helper;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Helper;
using System.Security.Cryptography;
using EntityFX.MqttY.Utils;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class MqttConfBenchmarkTests
    {
        private DijkstraWeightedIndexPathFinder pathFinder;
        private INetworkLogger? _monitoring;
        private TicksOptions tickOptions;
        private INetworkLoggerProvider? _monitoringProvider;
        private NetworkSimulator? _graph;

        private Exception? _testException;

        private bool IsParallelRefresh = true;
        private MqttTopicEvaluator mqttTopicEvaluator;
        private MqttNativePacketManager mqttPacketManager;

        public MqttConfBenchmarkTests()
        {
            pathFinder = new DijkstraWeightedIndexPathFinder();

            _monitoring = new NullNetworkLogger();
            tickOptions = new TicksOptions()
            {
                NetworkTicks = 2,
                TickPeriod = TimeSpan.FromMilliseconds(1)
            };
            _graph = new NetworkSimulator(pathFinder, _monitoring, tickOptions);


            _monitoringProvider = new NullNetworkLoggerProvider(_monitoring);
            _monitoringProvider.Start();

            mqttTopicEvaluator = new MqttTopicEvaluator(true);
            mqttPacketManager = new MqttNativePacketManager(mqttTopicEvaluator);
        }

        [TestInitialize]
        public void Initialize()
        {



        }

        [TestCleanup]
        public void Cleanup()
        {
            _graph!.Clear();
        }

        private void InitGraphTree(int branches, int depth, int clients, int servers)
        {
            var builder = new MqttNetworkBuilder(_graph!, mqttPacketManager, mqttTopicEvaluator);

            _graph!.Construction = true;
            var networks = builder.BuildTree(branches, depth, clients, servers, true, tickOptions);
            _graph.Construction = false;
            _graph.UpdateRoutes();
        }

        [TestMethod]
        public void MqttBenchmarkTest()
        {
            InitGraphTree(4, 2, 3, 2);

            var netsWithClients = _graph!.Networks.Values.Where(n => n.Clients.Count > 0).ToArray();

            var netsWithServers = _graph!.Networks.Values.Where(n => n.Servers.Count > 0).ToArray();

            Dictionary<string, string> clToSrvMap = GetClientToBrokerMap(netsWithClients, netsWithServers);

            foreach (var clientKv in clToSrvMap)
            {
                var client = _graph!.GetNode(clientKv.Key, NodeType.Client) as IMqttClient;
                Assert.IsNotNull(client);

                var broker = _graph.GetNode(clientKv.Value, NodeType.Server) as IMqttBroker;
                Assert.IsNotNull(broker);

                client.BeginConnect(broker.Name, false);
            }


            for (int i = 0; i < 20; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
                //_graph.Refresh(IsParallelRefresh);
            }

            //Assert.IsTrue(mqc1426.IsConnected);

            //var mqc1426C = GetMqttCounters(mqc1426);
            //var mqb782S = GetMqttCounters(mqb782);


            //Assert.AreEqual(mqc1426C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            //Assert.AreEqual(mqb782S!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            //Assert.AreEqual(mqc1426C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            //Assert.AreEqual(mqb782S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);

            var plantUmlGraphGenerator = new SimpleGraphMlGenerator();
            var uml = plantUmlGraphGenerator.SerializeNetworkGraph(_graph!);

            Console.WriteLine(_graph.Counters.PrintCounters());
        }

        private static Dictionary<string, string> GetClientToBrokerMap(INetwork[] netsWithClients, INetwork[] netsWithServers)
        {
            var clToSrvMap = new Dictionary<string, string>();
            foreach (var netWithClients in netsWithClients)
            {
                foreach (var client in netWithClients.Clients)
                {
                    var serverNetwork = netsWithServers[0];
                    do
                    {
                        var netServerIx = RandomNumberGenerator.GetInt32(0, netsWithServers.Length);
                        serverNetwork = netsWithServers[netServerIx];
                    } while (serverNetwork.Index == netWithClients.Index);

                    var serverIx = RandomNumberGenerator.GetInt32(0, serverNetwork.Servers.Count);
                    clToSrvMap.Add(client.Key, serverNetwork.Servers.Keys.ToArray()[serverIx]);
                }
            }

            return clToSrvMap;
        }
    }

}