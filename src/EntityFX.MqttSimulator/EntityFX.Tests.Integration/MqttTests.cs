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
        private ServiceProvider? _serviceProvider;
        private NetworkLogger? _monitoring;
        private ConsoleNetworkLoggerProvider? _monitoringProvider;
        private NetworkSimulator? _graph;

        private Exception? _testException;

        [TestInitialize]
        public void Initialize()
        {
            var pathFinder = new DijkstraPathFinder();
            _monitoring = new NetworkLogger(true, TimeSpan.FromMilliseconds(1), new MonitoringIgnoreOption());
            var tickOptions = new TicksOptions()
            {
                OutgoingWaitTicks = 2,
                TickPeriod = TimeSpan.FromMilliseconds(1)
            };
            _graph = new NetworkSimulator(pathFinder, _monitoring, tickOptions);


            _monitoringProvider = new ConsoleNetworkLoggerProvider(_monitoring);
            _monitoringProvider.Start();

            var mqttTopicEvaluator = new MqttTopicEvaluator(true);
            var mqttPacketManager = new MqttNativePacketManager(mqttTopicEvaluator);

            var network1 = new Network(0, "net1", "net1.local", "eth", new NetworkTypeOption() {
                NetworkType = "eth", TransferTicks = 3, Speed = 18750000
            }, tickOptions);
            _graph.AddNetwork(network1);

            var mqc1 = new MqttClient(mqttPacketManager, 0, "mqc1", "mqtt://mqc1.net1.local",
                "mqtt", "mqtt", "mqc1", tickOptions);
            network1.AddClient(mqc1);

            var mqs1 = new MqttBroker(mqttPacketManager, mqttTopicEvaluator, 0, "mqs1", "mqtt://mqs1.net1.local",
                "mqtt", "mqtt", tickOptions);
            network1.AddServer(mqs1);

            _graph.AddClient(mqc1);
            _graph.AddServer(mqs1);

            _graph!.OnError += (sender, e) =>
            {
                _testException = e;
            };

            var json = JsonSerializer.Serialize(_graph, new JsonSerializerOptions() {  ReferenceHandler = ReferenceHandler.Preserve });

            _ = _graph.StartPeriodicRefreshAsync();
        }

        [TestMethod]
        public void MqttConnectTest()
        {
            var mqClient = _graph!.GetNode("mqc1", NodeType.Client) as IMqttClient;

            Assert.IsNotNull(mqClient);

            mqClient.Connect("mqs1");

            Assert.IsTrue(mqClient.IsConnected);
        }
    }
}