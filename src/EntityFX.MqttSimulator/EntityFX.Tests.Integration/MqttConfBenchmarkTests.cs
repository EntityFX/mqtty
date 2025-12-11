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
using System.Text;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Contracts.Utils;

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
        private NetworkOptions _networkOptions;
        private StringBuilder _logSb;
        private Exception? _testException;

        private bool IsParallelRefresh = true;
        private MqttTopicEvaluator mqttTopicEvaluator;
        private MqttNativePacketManager mqttPacketManager;
        private IClientBuilder clientBuilder;

        public MqttConfBenchmarkTests()
        {
            pathFinder = new DijkstraWeightedIndexPathFinder();

            _monitoring = new NetworkLogger(false, TimeSpan.FromMilliseconds(1), new MonitoringIgnoreOption() { Category = new string[] { "Refresh", "Link" } });

            //_monitoring = new NetworkLogger(false, TimeSpan.FromMilliseconds(1), new MonitoringIgnoreOption());
            //_monitoringProvider = new ConsoleNetworkLoggerProvider(_monitoring);
            //_monitoringProvider.Start();

            tickOptions = new TicksOptions()
            {
                OutgoingWaitTicks = 5,
                TickPeriod = TimeSpan.FromMilliseconds(1)
            };
            _graph = new NetworkSimulator(pathFinder, _monitoring, tickOptions);

            _networkOptions = new NetworkOptions()
            {
                NetworkType = "eth",
                TransferTicks = 2,
                Speed = 18750000
            };

            clientBuilder = new ActionClientBuilder(AppBuild);

            _logSb = new StringBuilder();

            _monitoringProvider = new SimpleNetworkLoggerProvider(_monitoring, _logSb);
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



        }

        [TestCleanup]
        public void Cleanup()
        {
            _graph!.Clear();
        }

        private void InitGraphTree(int branches, int depth, int clients, int servers, Dictionary<string, int>? appsPerNode = null)
        {
            var builder = new MqttNetworkBuilder(_graph!, mqttPacketManager, mqttTopicEvaluator, clientBuilder);

            _graph!.Construction = true;
            var networks = builder.BuildTree(branches, depth, clients, servers, appsPerNode, true, tickOptions, _networkOptions);
            _graph.Construction = false;
            _graph.UpdateRoutes();
        }

        private void InitChain(int branches, int length, int clients, int servers, Dictionary<string, int>? appsPerNod = null)
        {
            var builder = new MqttNetworkBuilder(_graph!, mqttPacketManager, mqttTopicEvaluator, clientBuilder);

            _graph!.Construction = true;
            var networks = builder.BuildChain(branches, length, clients, servers, appsPerNod, true, tickOptions, _networkOptions);
            _graph.Construction = false;
            _graph.UpdateRoutes();
        }

        private void InitLine(int length, int clients, int servers, Dictionary<string, int>? appsPerNode = null)
        {
            var builder = new MqttNetworkBuilder(_graph!, mqttPacketManager, mqttTopicEvaluator, clientBuilder);

            _graph!.Construction = true;
            var networks = builder.BuildLine(length, clients, servers, appsPerNode, true, tickOptions, _networkOptions);
            _graph.Construction = false;
            _graph.UpdateRoutes();
        }

        [TestMethod]
        public void MqttBenchmarkTest()
        {
            InitGraphTree(2, 3, 1, 1);

            var netsWithClients = _graph!.Networks.Values.Where(n => n.Clients.Count > 0).ToArray();

            var netsWithServers = _graph!.Networks.Values.Where(n => n.Servers.Count > 0).ToArray();
            
            //var clToSrvMap = GetClientToBrokerConnectionMapRandom(netsWithClients, netsWithServers);
            var clToSrvMap = GetClientToBrokerConnectionMapOposite(netsWithClients, netsWithServers);

            foreach (var clientKv in clToSrvMap)
            {
                var client = _graph!.GetNode(clientKv.Key, NodeType.Client) as IMqttClient;
                Assert.IsNotNull(client);

                var broker = _graph.GetNode(clientKv.Value, NodeType.Server) as IMqttBroker;
                Assert.IsNotNull(broker);

                client.BeginConnect(broker.Name, false);
            }

            var allConnected = false;
            while (!allConnected)
            {
                allConnected = _graph!.Clients.All(c => c.Value.IsConnected);
                //var nonConnected = _graph!.Clients.Where(c => !c.Value.IsConnected);
                _graph.RefreshWithCounters(IsParallelRefresh);
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

        [TestMethod]
        public void MqttChainTest()
        {
            InitChain(1, 3, 1, 1);

            var netsWithClients = _graph!.Networks.Values.Where(n => n.Clients.Count > 0).ToArray();

            var netsWithServers = _graph!.Networks.Values.Where(n => n.Servers.Count > 0).ToArray();

            //var clToSrvMap = GetClientToBrokerConnectionMapRandom(netsWithClients, netsWithServers);
            var clToSrvMap = GetClientToBrokerConnectionMapOposite(netsWithClients, netsWithServers);

            foreach (var clientKv in clToSrvMap)
            {
                var client = _graph!.GetNode(clientKv.Key, NodeType.Client) as IMqttClient;
                Assert.IsNotNull(client);

                var broker = _graph.GetNode(clientKv.Value, NodeType.Server) as IMqttBroker;
                Assert.IsNotNull(broker);

                client.BeginConnect(broker.Name, false);
            }

            var allConnected = false;
            while (!allConnected)
            {
                allConnected = _graph!.Clients.All(c => c.Value.IsConnected);
                //var nonConnected = _graph!.Clients.Where(c => !c.Value.IsConnected);
                _graph.RefreshWithCounters(IsParallelRefresh);
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


        [TestMethod]
        public void MqttLineTest()
        {
            InitLine(2, 1, 1);

            var netsWithClients = _graph!.Networks.Values.Where(n => n.Clients.Count > 0).ToArray();

            var netsWithServers = _graph!.Networks.Values.Where(n => n.Servers.Count > 0).ToArray();

            //var clToSrvMap = GetClientToBrokerConnectionMapRandom(netsWithClients, netsWithServers);
            var clToSrvMap = GetClientToBrokerConnectionMapOposite(netsWithClients, netsWithServers);

            foreach (var clientKv in clToSrvMap)
            {
                var client = _graph!.GetNode(clientKv.Key, NodeType.Client) as IMqttClient;
                Assert.IsNotNull(client);

                var broker = _graph.GetNode(clientKv.Value, NodeType.Server) as IMqttBroker;
                Assert.IsNotNull(broker);

                client.BeginConnect(broker.Name, false);
            }

            var allConnected = false;
            while (!allConnected)
            {
                allConnected = _graph!.Clients.All(c => c.Value.IsConnected);
                _graph.RefreshWithCounters(IsParallelRefresh);
            }



            var plantUmlGraphGenerator = new SimpleGraphMlGenerator();
            var uml = plantUmlGraphGenerator.SerializeNetworkGraph(_graph!);

            Console.WriteLine(_logSb.ToString());
        }


        private static Dictionary<string, string> GetClientToBrokerConnectionMapRandom(INetwork[] netsWithClients, INetwork[] netsWithServers)
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

        private static Dictionary<string, string> GetClientToBrokerConnectionMapOposite(INetwork[] netsWithClients, INetwork[] netsWithServers)
        {
            var netsWithServersReversed = netsWithServers.Reverse().ToArray();
            var clToSrvMap = new Dictionary<string, string>(); 

            var netSrvIndex = 0;
            foreach (var netWithClients in netsWithClients)
            {
                var serverNetwork = netsWithServersReversed[netSrvIndex];
                var servers = serverNetwork.Servers.Values.ToArray();

                if (netWithClients.Id == serverNetwork.Id)
                {
                    netSrvIndex++;
                    if (netSrvIndex >= netsWithServersReversed.Length)
                    {
                        netSrvIndex = 0;
                    }
                }

                var srvIndex = 0;
                foreach (var client in netWithClients.Clients)
                {
                    var server = servers[srvIndex];
                    clToSrvMap.Add(client.Key, server.Name);

                    srvIndex++;
                    if (srvIndex >= servers.Length)
                    {
                        srvIndex = 0;
                    }

                }

                netSrvIndex++;
                if (netSrvIndex >= netsWithServersReversed.Length)
                {
                    netSrvIndex = 0;
                }
            }

            return clToSrvMap;
        }
    }

}