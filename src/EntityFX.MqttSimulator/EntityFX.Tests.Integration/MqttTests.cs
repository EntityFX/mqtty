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
    public class MqttTests
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

            var network1 = new Network(0, "net1", "net1.local", "eth", new NetworkTypeOption() {
                NetworkType = "eth", RefreshTicks = 2, SendTicks = 3, Speed = 18750000
            }, tickOptions);
            graph.AddNetwork(network1);

            var mqc1 = new MqttClient(mqttPacketManager, 0, "mqc1", "mqtt://mqc1.net1.local",
                "mqtt", "mqtt", "mqc1", tickOptions);
            network1.AddClient(mqc1);

            var mqs1 = new MqttBroker(mqttPacketManager, mqttTopicEvaluator, 0, "mqs1", "mqtt://mqs1.net1.local",
                "mqtt", "mqtt", tickOptions);
            network1.AddServer(mqs1);

            graph.AddClient(mqc1);
            graph.AddServer(mqs1);

            graph!.OnError += (sender, e) =>
            {
                testException = e;
            };

            var json = JsonSerializer.Serialize(graph, new JsonSerializerOptions() {  ReferenceHandler = ReferenceHandler.Preserve });

            _ = graph.StartPeriodicRefreshAsync();
        }

        [TestMethod]
        public void MqttConnectTest()
        {
            var mqClient = graph!.GetNode("mqc1", NodeType.Client) as IMqttClient;

            Assert.IsNotNull(mqClient);

            mqClient.Connect("mqs1");

            Assert.IsTrue(mqClient.IsConnected);
        }
    }
}