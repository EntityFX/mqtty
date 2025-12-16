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

        private bool IsParallelRefresh = false;

        [TestInitialize]
        public void Initialize()
        {
            var pathFinder = new DijkstraPathFinder();
            var pathFinder2 = new DijkstraWeightedIndexPathFinder();
            //_monitoring = new NetworkLogger(false, TimeSpan.FromMilliseconds(1), new MonitoringIgnoreOption() { 
            //    Category = new string[] { "Refresh" } });
            _monitoring = new NullNetworkLogger();
            var tickOptions = new TicksOptions()
            {
                OutgoingWaitTicks = 2,
                TickPeriod = TimeSpan.FromMilliseconds(1)
            };
            _graph = new NetworkSimulator(pathFinder2, _monitoring, tickOptions, true);


            _monitoringProvider = new NullNetworkLoggerProvider(_monitoring);
            _monitoringProvider.Start();

            var mqttTopicEvaluator = new MqttTopicEvaluator(true);
            var mqttPacketManager = new MqttNativePacketManager(mqttTopicEvaluator);

            var netGlobal = new Network(0, "net.global", "net.global", "eth", new NetworkOptions()
            {
                NetworkType = "eth",
                TransferTicks = 3,
                Speed = 18750000
            }, tickOptions);
            _graph.AddNetwork(netGlobal);

            var net1Local = new Network(1, "net1.local", "net1.local", "eth", new NetworkOptions()
            {
                NetworkType = "eth",
                TransferTicks = 3,
                Speed = 18750000
            }, tickOptions);
            _graph.AddNetwork(net1Local);

            var net2Local = new Network(2, "net2.local", "net2.local", "eth", new NetworkOptions()
            {
                NetworkType = "eth",
                TransferTicks = 3,
                Speed = 18750000
            }, tickOptions);
            _graph.AddNetwork(net2Local);

            var net3Local = new Network(3, "net3.local", "net3.local", "eth", new NetworkOptions()
            {
                NetworkType = "eth",
                TransferTicks = 3,
                Speed = 18750000
            }, tickOptions);
            _graph.AddNetwork(net3Local);

            var net4Local = new Network(3, "net4.local", "net4.local", "eth", new NetworkOptions()
            {
                NetworkType = "eth",
                TransferTicks = 3,
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

            var mqc1Net3 = new MqttClient(mqttPacketManager, 0, "mqc1.net3.local", "mqtt://mqc1.net3.local",
                "mqtt", "mqtt", "mqc1net3", tickOptions);
            net3Local.AddClient(mqc1Net3);

            var mqs1Net1 = new MqttBroker(mqttPacketManager, mqttTopicEvaluator, 0, "mqs1.net1.local", "mqtt://mqs1.net1.local",
                "mqtt", "mqtt", tickOptions);
            net1Local.AddServer(mqs1Net1);

            var mqs1Net3 = new MqttBroker(mqttPacketManager, mqttTopicEvaluator, 0, "mqs1.net3.local", "mqtt://mqs1.net3.local",
                "mqtt", "mqtt", tickOptions);
            net3Local.AddServer(mqs1Net3);

            _graph.AddClient(mqc1Net2);
            _graph.AddClient(mqc1Net4);
            _graph.AddClient(mqc1Net3);
            _graph.AddServer(mqs1Net1);
            _graph.AddServer(mqs1Net3);

            _graph!.OnError += (sender, e) =>
            {
                _testException = e;
            };

            _graph.UpdateRoutes();
        }

        [TestMethod]
        public void MqttConnectSingleTest()
        {
            _graph!.Counters.Clear();

            var mqc1Net2 = _graph!.GetNode("mqc1.net2.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net2);
            mqc1Net2.BeginConnect("mqs1.net3.local", false);

            var mqs1Net3 = _graph.GetNode("mqs1.net3.local", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqs1Net3);

            for (int i = 0; i < 27; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
            }


            var m1N2C = GetMqttCounters(mqc1Net2);
            var m1N3S = GetMqttCounters(mqs1Net3);

            foreach (var item in _monitoring!.Items)
            {
                Console.WriteLine(item.GetMonitoringLine());
            }

            //_monitoring.


            Console.WriteLine(_graph.Counters.PrintCounters());


            Assert.IsTrue(mqc1Net2.IsConnected);

            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);

            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);


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

            for (int i = 0; i < 27; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
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

            for (int i = 0; i < 27; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
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

            for (int i = 0; i < 27; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
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


        [TestMethod]
        public void MqttConnectSubscribePublishTest()
        {
            var mqc1Net2 = _graph!.GetNode("mqc1.net2.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net2);
            mqc1Net2.BeginConnect("mqs1.net3.local", false);

            var mqc1Net4 = _graph!.GetNode("mqc1.net4.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net4);
            mqc1Net4.BeginConnect("mqs1.net1.local", false);

            var mqc1Net3 = _graph!.GetNode("mqc1.net3.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net3);
            mqc1Net3.BeginConnect("mqs1.net1.local", false);

            var mqs1Net1 = _graph.GetNode("mqs1.net1.local", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqs1Net1);

            var mqs1Net3 = _graph.GetNode("mqs1.net3.local", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqs1Net3);

            for (int i = 0; i < 27; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
            }

            Assert.IsTrue(mqc1Net2.IsConnected);
            Assert.IsTrue(mqc1Net4.IsConnected);
            Assert.IsTrue(mqc1Net3.IsConnected);

            var m1N2C = GetMqttCounters(mqc1Net2);
            var m1N4C = GetMqttCounters(mqc1Net4);
            var m1N3C = GetMqttCounters(mqc1Net3);
            var m1N1S = GetMqttCounters(mqs1Net1);
            var m1N3S = GetMqttCounters(mqs1Net3);

            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);

            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.Connect].Value, 2);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 2);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);

            Assert.AreEqual(m1N3C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N3C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);

            mqc1Net2.BeginSubscribe("/test2/#", MqttQos.AtLeastOnce);
            mqc1Net4.BeginSubscribe("/test4/#", MqttQos.AtLeastOnce);

            for (int i = 0; i < 27; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
            }

            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);

            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);

            mqc1Net3.Publish("/test4/data1", new byte[] { 1, 2, 3, 4, 5 }, MqttQos.AtLeastOnce);

            mqc1Net4.MessageReceived += (object? sender, MqttMessage e) =>
            {
                CollectionAssert.AreEqual(e.Payload, new byte[] { 1, 2, 3, 4, 5 });
            };

            for (int i = 0; i < 27; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
            }

            Console.WriteLine(_graph.Counters.PrintCounters());
        }

        [TestMethod]
        public void MqttConnectSubscribePublishLongTest()
        {
            var mqc1Net2 = _graph!.GetNode("mqc1.net2.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net2);
            mqc1Net2.BeginConnect("mqs1.net3.local", false);

            var mqc1Net4 = _graph!.GetNode("mqc1.net4.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net4);
            mqc1Net4.BeginConnect("mqs1.net1.local", false);

            var mqc1Net3 = _graph!.GetNode("mqc1.net3.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1Net3);
            mqc1Net3.BeginConnect("mqs1.net1.local", false);

            var mqs1Net1 = _graph.GetNode("mqs1.net1.local", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqs1Net1);

            var mqs1Net3 = _graph.GetNode("mqs1.net3.local", NodeType.Server) as IMqttBroker;
            Assert.IsNotNull(mqs1Net3);

            for (int i = 0; i < 27; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
            }

            Assert.IsTrue(mqc1Net2.IsConnected);
            Assert.IsTrue(mqc1Net4.IsConnected);
            Assert.IsTrue(mqc1Net3.IsConnected);

            var m1N2C = GetMqttCounters(mqc1Net2);
            var m1N4C = GetMqttCounters(mqc1Net4);
            var m1N3C = GetMqttCounters(mqc1Net3);
            var m1N1S = GetMqttCounters(mqs1Net1);
            var m1N3S = GetMqttCounters(mqs1Net3);

            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);

            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.Connect].Value, 2);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 2);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);

            Assert.AreEqual(m1N3C!.PacketTypeCounters[MqttPacketType.Connect].Value, 1);
            Assert.AreEqual(m1N3C!.PacketTypeCounters[MqttPacketType.ConnectAck].Value, 1);

            mqc1Net2.BeginSubscribe("/test2/#", MqttQos.AtLeastOnce);
            mqc1Net4.BeginSubscribe("/test4/#", MqttQos.AtLeastOnce);

            for (int i = 0; i < 27; i++)
            {
                _graph.RefreshWithCounters(IsParallelRefresh);
            }

            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N2C!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);
            Assert.AreEqual(m1N4C!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);

            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.Subscribe].Value, 1);
            Assert.AreEqual(m1N1S!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);
            Assert.AreEqual(m1N3S!.PacketTypeCounters[MqttPacketType.SubscribeAck].Value, 1);



            mqc1Net4.MessageReceived += (object? sender, MqttMessage e) =>
            {
                CollectionAssert.AreEqual(e.Payload, new byte[] { 1, 2, 3, 4, 5 });
            };

            for (int n = 0; n < 3000; n++)
            {
                mqc1Net3.Publish("/test4/data1", new byte[] { 1, 2, 3, 4, 5 }, MqttQos.AtLeastOnce);
                for (int i = 0; i < 27; i++)
                {
                    _graph.RefreshWithCounters(false);
                }
            }

            Console.WriteLine(_graph.Counters.PrintCounters());
        }

    }
}