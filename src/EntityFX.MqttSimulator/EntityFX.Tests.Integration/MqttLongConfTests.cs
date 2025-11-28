using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.Tests.Integration.Helpers;
using EntityFX.MqttY.Utils;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class MqttLongConfTests
    {
        private INetworkLogger? _monitoring;
        private TicksOptions tickOptions;
        private INetworkLoggerProvider? _monitoringProvider;
        private NetworkSimulator? _graph;

        private Exception? _testException;

        private bool IsParallelRefresh = false;

        [TestInitialize]
        public void Initialize()
        {
            var pathFinder = new DijkstraPathFinder();

            _monitoring = new NullNetworkLogger();
            tickOptions = new TicksOptions()
            {
                NetworkTicks = 2,
                TickPeriod = TimeSpan.FromMilliseconds(1)
            };
            _graph = new NetworkSimulator(pathFinder, _monitoring, tickOptions);


            _monitoringProvider = new NullNetworkLoggerProvider(_monitoring);
            _monitoringProvider.Start();

            var mqttTopicEvaluator = new MqttTopicEvaluator(true);
            var mqttPacketManager = new MqttNativePacketManager(mqttTopicEvaluator);

            var builder = new MqttNetworkBulder(_graph!, mqttPacketManager, mqttTopicEvaluator);
            var networks = builder.BuildTree(5, 4, 10, 1, tickOptions);

        }

        [TestMethod]
        public void SimpleCommunicationTest()
        {


            var plantUmlGraphGenerator = new SimpleGraphMLGenerator();
            var uml = plantUmlGraphGenerator.Generate(_graph!); 
        }


    }
}