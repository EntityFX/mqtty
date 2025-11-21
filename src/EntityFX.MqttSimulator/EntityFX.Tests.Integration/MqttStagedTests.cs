using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class MqttStagedTests
    {
        private ServiceProvider? serviceProvider;
        private NetworkLogger? monitoring;
        private ConsoleNetworkLoggerProvider? monitoringProvider;
        private NetworkSimulator? graph;

        private Exception? testException;

        [TestInitialize]
        public void Initialize()
        {
            var pathFinder = new DijkstraPathFinder();
            monitoring = new NetworkLogger(true, TimeSpan.FromMilliseconds(1), new MonitoringIgnoreOption());
            var tickOptions = new TicksOptions()
            {
                NetworkTicks = 2,
                TickPeriod = TimeSpan.FromMilliseconds(1)
            };
            graph = new NetworkSimulator(pathFinder, monitoring, tickOptions);


            monitoringProvider = new ConsoleNetworkLoggerProvider(monitoring);
            monitoringProvider.Start();

            var mqttTopicEvaluator = new MqttTopicEvaluator(true);
            var mqttPacketManager = new MqttNativePacketManager(mqttTopicEvaluator);

            var netGlobal = new Network(0, "net.global", "net.global", "eth", new NetworkTypeOption()
            {
                NetworkType = "eth",
                RefreshTicks = 2,
                SendTicks = 3,
                Speed = 18750000
            }, tickOptions);
            graph.AddNetwork(netGlobal);

            var net1Local = new Network(1, "net1.local", "net1.local", "eth", new NetworkTypeOption()
            {
                NetworkType = "eth",
                RefreshTicks = 2,
                SendTicks = 3,
                Speed = 18750000
            }, tickOptions);
            graph.AddNetwork(net1Local);

            var net2Local = new Network(2, "net2.local", "net2.local", "eth", new NetworkTypeOption()
            {
                NetworkType = "eth",
                RefreshTicks = 2,
                SendTicks = 3,
                Speed = 18750000
            }, tickOptions);
            graph.AddNetwork(net2Local);

            var net3Local = new Network(3, "net3.local", "net3.local", "eth", new NetworkTypeOption()
            {
                NetworkType = "eth",
                RefreshTicks = 2,
                SendTicks = 3,
                Speed = 18750000
            }, tickOptions);
            graph.AddNetwork(net3Local);

            var net4Local = new Network(3, "net4.local", "net4.local", "eth", new NetworkTypeOption()
            {
                NetworkType = "eth",
                RefreshTicks = 2,
                SendTicks = 3,
                Speed = 18750000
            }, tickOptions);
            graph.AddNetwork(net4Local);

            netGlobal.Link(net1Local);
            netGlobal.Link(net2Local);
            netGlobal.Link(net3Local);
            netGlobal.Link(net4Local);

            var mqc1_net2 = new MqttClient(mqttPacketManager, 0, "mqc1.net2.local", "mqtt://mqc1.net2.local",
                "mqtt", "mqtt", "mqc1net2", tickOptions);
            net2Local.AddClient(mqc1_net2);

            var mqc1_net4 = new MqttClient(mqttPacketManager, 0, "mqc1.net4.local", "mqtt://mqc1.net4.local",
                "mqtt", "mqtt", "mqc1net4", tickOptions);
            net4Local.AddClient(mqc1_net4);

            var mqs1_net1 = new MqttBroker(mqttPacketManager, mqttTopicEvaluator, 0, "mqs1.net1.local", "mqtt://mqs1.net1.local",
                "mqtt", "mqtt", tickOptions);
            net1Local.AddServer(mqs1_net1);

            var mqs1_net3 = new MqttBroker(mqttPacketManager, mqttTopicEvaluator, 0, "mqs1.net3.local", "mqtt://mqs1.net3.local",
                "mqtt", "mqtt", tickOptions);
            net3Local.AddServer(mqs1_net3);

            graph.AddClient(mqc1_net2);
            graph.AddClient(mqc1_net4);
            graph.AddServer(mqs1_net1);
            graph.AddServer(mqs1_net3);

            graph!.OnError += (sender, e) =>
            {
                testException = e;
            };

            var json = JsonSerializer.Serialize(graph, new JsonSerializerOptions() { ReferenceHandler = ReferenceHandler.Preserve });

            graph.UpdateRoutes();
        }


        [TestMethod]
        public void MqttConnectTest()
        {
            var mqc1_net2 = graph!.GetNode("mqc1.net2.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1_net2);
            mqc1_net2.BeginConnect("mqs1.net3.local", false);

            var mqc1_net4 = graph!.GetNode("mqc1.net4.local", NodeType.Client) as IMqttClient;
            Assert.IsNotNull(mqc1_net4);
            mqc1_net4.BeginConnect("mqs1.net1.local", false);

            for (int i = 0; i < 7; i++)
            {
                graph.RefreshWithCounters();
            }

            Assert.IsTrue(mqc1_net2.IsConnected);
            Assert.IsTrue(mqc1_net4.IsConnected);
        }
    }
}