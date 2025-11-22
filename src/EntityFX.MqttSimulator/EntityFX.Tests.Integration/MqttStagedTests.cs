using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;
using System.Text.Json;
using System.Text.Json.Serialization;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Counter;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Plugin.Mqtt.Counter;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class MqttStagedTests
    {
        private INetworkLogger? _monitoring;
        private INetworkLoggerProvider? _monitoringProvider;
        private NetworkSimulator? _graph;

        private Exception? _testException;

        [TestInitialize]
        public void Initialize()
        {
            var pathFinder = new DijkstraPathFinder();
            //_monitoring = new NetworkLogger(true, TimeSpan.FromMilliseconds(1), new MonitoringIgnoreOption());
            _monitoring = new NullNetworkLogger();
            var tickOptions = new TicksOptions()
            {
                NetworkTicks = 2,
                TickPeriod = TimeSpan.FromMilliseconds(1)
            };
            _graph = new NetworkSimulator(pathFinder, _monitoring, tickOptions);


            _monitoringProvider = new NullNetworkLoggerProvider(_monitoring);
            _monitoringProvider.Start();

            var mqttTopicEvaluator = new MqttTopicEvaluator(true);
            var mqttPacketManager = new MqttNativePacketManager(mqttTopicEvaluator);

            var netGlobal = new Network(0, "net.global", "net.global", "eth", new NetworkTypeOption()
            {
                NetworkType = "eth",
                RefreshTicks = 2,
                SendTicks = 3,
                Speed = 18750000
            }, tickOptions);
            _graph.AddNetwork(netGlobal);

            var net1Local = new Network(1, "net1.local", "net1.local", "eth", new NetworkTypeOption()
            {
                NetworkType = "eth",
                RefreshTicks = 2,
                SendTicks = 3,
                Speed = 18750000
            }, tickOptions);
            _graph.AddNetwork(net1Local);

            var net2Local = new Network(2, "net2.local", "net2.local", "eth", new NetworkTypeOption()
            {
                NetworkType = "eth",
                RefreshTicks = 2,
                SendTicks = 3,
                Speed = 18750000
            }, tickOptions);
            _graph.AddNetwork(net2Local);

            var net3Local = new Network(3, "net3.local", "net3.local", "eth", new NetworkTypeOption()
            {
                NetworkType = "eth",
                RefreshTicks = 2,
                SendTicks = 3,
                Speed = 18750000
            }, tickOptions);
            _graph.AddNetwork(net3Local);

            var net4Local = new Network(3, "net4.local", "net4.local", "eth", new NetworkTypeOption()
            {
                NetworkType = "eth",
                RefreshTicks = 2,
                SendTicks = 3,
                Speed = 18750000
            }, tickOptions);
            _graph.AddNetwork(net4Local);

            netGlobal.Link(net1Local);
            netGlobal.Link(net2Local);
            netGlobal.Link(net3Local);
            netGlobal.Link(net4Local);

            var mqc1Net2 = new MqttClient(mqttPacketManager, 0, "mqc1.net2.local", "mqtt://mqc1.net2.local",
                "mqtt", "mqtt", "mqc1net2", tickOptions);
            net2Local.AddClient(mqc1Net2);

            var mqc1Net4 = new MqttClient(mqttPacketManager, 0, "mqc1.net4.local", "mqtt://mqc1.net4.local",
                "mqtt", "mqtt", "mqc1net4", tickOptions);
            net4Local.AddClient(mqc1Net4);

            var mqs1Net1 = new MqttBroker(mqttPacketManager, mqttTopicEvaluator, 0, "mqs1.net1.local", "mqtt://mqs1.net1.local",
                "mqtt", "mqtt", tickOptions);
            net1Local.AddServer(mqs1Net1);

            var mqs1Net3 = new MqttBroker(mqttPacketManager, mqttTopicEvaluator, 0, "mqs1.net3.local", "mqtt://mqs1.net3.local",
                "mqtt", "mqtt", tickOptions);
            net3Local.AddServer(mqs1Net3);

            _graph.AddClient(mqc1Net2);
            _graph.AddClient(mqc1Net4);
            _graph.AddServer(mqs1Net1);
            _graph.AddServer(mqs1Net3);

            _graph!.OnError += (sender, e) =>
            {
                _testException = e;
            };

            var json = JsonSerializer.Serialize(_graph, new JsonSerializerOptions() { ReferenceHandler = ReferenceHandler.Preserve });

            _graph.UpdateRoutes();
        }


        [TestMethod]
        public void MqttConnectTest()
        {
            _graph!.Counters.Clear();
            
            var mqc1Net2 = _graph!.GetNode("mqc1.net2.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net2);
            mqc1Net2.BeginConnect("mqs1.net3.local", false);

            var mqc1Net4 = _graph!.GetNode("mqc1.net4.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net4);
            mqc1Net4.BeginConnect("mqs1.net1.local", false);

            var mqs1Net1 = _graph.GetNode("mqs1.net1.local", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqs1Net1);
            
            var mqs1Net3 = _graph.GetNode("mqs1.net3.local", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqs1Net3);

            for (int i = 0; i < 18; i++)
            {
                _graph.RefreshWithCounters();
            }
            
            Thread.Sleep(10);
            
            _graph.RefreshWithCounters();

            Assert.IsTrue(mqc1Net2.IsConnected);
            Assert.IsTrue(mqc1Net4.IsConnected);

            var m1N2C = GetMqttCounters(mqc1Net2);
            var m1N4C = GetMqttCounters(mqc1Net4);
            var m1N1S = GetMqttCounters(mqs1Net1);
            var m1N3S = GetMqttCounters(mqs1Net3);
            
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            
            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            
            Console.WriteLine(_graph.Counters.PrintCounters());
        }

        private static MqttCounters? GetMqttCounters(INode node)
        {
            return (node.Counters as NodeCounters)?.Counters.OfType<MqttCounters>().FirstOrDefault();
        }

        [TestMethod]
        public void MqttConnectSubscribeTest()
        {
            var mqc1Net2 = _graph!.GetNode("mqc1.net2.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net2);
            mqc1Net2.BeginConnect("mqs1.net3.local", false);

            var mqc1Net4 = _graph!.GetNode("mqc1.net4.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net4);
            mqc1Net4.BeginConnect("mqs1.net1.local", false);
            
            var mqs1Net1 = _graph.GetNode("mqs1.net1.local", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqs1Net1);
            
            var mqs1Net3 = _graph.GetNode("mqs1.net3.local", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqs1Net3);

            for (int i = 0; i < 18; i++)
            {
                _graph.RefreshWithCounters();
            }

            Assert.IsTrue(mqc1Net2.IsConnected);
            Assert.IsTrue(mqc1Net4.IsConnected);
            
            var m1N2C = GetMqttCounters(mqc1Net2);
            var m1N4C = GetMqttCounters(mqc1Net4);
            var m1N1S = GetMqttCounters(mqs1Net1);
            var m1N3S = GetMqttCounters(mqs1Net3);
            
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            
            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            
            mqc1Net2.BeginSubscribe("/test2/#", MqttQos.AtLeastOnce);
            mqc1Net4.BeginSubscribe("/test4/#", MqttQos.AtLeastOnce);
            
            for (int i = 0; i < 18; i++)
            {
                _graph.RefreshWithCounters();
            }
            
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);
            
            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);
            
            Console.WriteLine(_graph.Counters.PrintCounters());
        }
    }
}