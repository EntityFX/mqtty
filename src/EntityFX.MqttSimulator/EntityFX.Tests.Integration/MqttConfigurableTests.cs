using EntityFX.MqttY;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Factories;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.Tests.Integration
{
    [TestClass]
    public class MqttConfigurableTests
    {
        private ServiceProvider? _serviceProvider;
        private NetworkLogger? _monitoring;
        private ConsoleNetworkLoggerProvider? _monitoringProvider;
        private NetworkSimulator? _graph;

        private Exception? _testException;

        [TestInitialize]
        public void Initialize()
        {
            var serviceCollection = new ServiceCollection()
                .AddScoped<INodesBuilder, NodesBuilder>((sp) =>
                {
                    return new NodesBuilder(
                        new Dictionary<string, IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>>()
                        {
                            ["net"] = new ClientFactory(),
                            ["mqtt"] = new MqttClientFactory(),
                        },
                        new Dictionary<string, IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>>()
                        {
                            ["net"] = new ServerFactory(sp),
                            ["mqtt"] = new MqttServerFactory(sp),
                        },
                        new Dictionary<string, IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>>()
                        {
                            ["net"] = new GenericApplicationFactiory(),
                            ["mqtt"] = new GenericApplicationFactiory(),
                        }, sp.GetRequiredService<IFactory<INetwork, NodeBuildOptions<NetworkBuildOption>>>()!);
                })
                .ConfigureMqttServices()
                .ConfigureServices();

            _serviceProvider = serviceCollection.BuildServiceProvider();


            _monitoring = new NetworkLogger(false, TimeSpan.FromMilliseconds(0.1), new MonitoringIgnoreOption()
            {
                Category = new string[] { "Refresh" }
            });
            _monitoringProvider = new ConsoleNetworkLoggerProvider(_monitoring);

            _monitoringProvider.Start();

            var networkBuilder = _serviceProvider?.GetRequiredService<INodesBuilder>();
            var networkSimulatorBuilder = _serviceProvider?.GetRequiredService<INetworkSimulatorBuilder>();

            _graph = new NetworkSimulator(new DijkstraWeightedIndexPathFinder(), _monitoring!,
                new TicksOptions()
                {
                    ReceiveWaitPeriod = TimeSpan.FromMilliseconds(0.1)
                });

            networkSimulatorBuilder!.Configure(_graph, new NetworkGraphOption()
            {
                Networks = new SortedDictionary<string, NetworkNodeOption>()
                {
                    ["n1"] = new NetworkNodeOption() { Index = 0, NetworkType = "1g", Links = new NetworkLinkOption[] { new NetworkLinkOption() { Network = "n2" } } },
                    ["n2"] = new NetworkNodeOption() { Index = 1, NetworkType = "1g", Links = new NetworkLinkOption[] { new NetworkLinkOption() { Network = "n3" } } },
                    ["n3"] = new NetworkNodeOption() { Index = 2, NetworkType = "1g", Links = Array.Empty<NetworkLinkOption>() },
                },
                Nodes = new SortedDictionary<string, NodeOption>()
                {
                    ["mqs1"] = new NodeOption() { Index = 0, Protocol = "mqtt", Type = NodeOptionType.Server, Specification = "mqtt-server", Network = "n3" },
                    ["mqc1"] = new NodeOption() { Index = 1, Protocol = "mqtt", Type = NodeOptionType.Client, Specification = "mqtt-client", Network = "n1" },
                    ["mqc2"] = new NodeOption() { Index = 2, Protocol = "mqtt", Type = NodeOptionType.Client, Specification = "mqtt-client", Network = "n2" },
                },
                NetworkTypes = new SortedDictionary<string, NetworkTypeOption>()
                {
                    ["1g"] = new NetworkTypeOption() { RefreshTicks = 2, SendTicks = 2, Speed = 125_000_000 }
                }
            });

            _graph!.OnError += (sender, e) =>
            {
                _testException = e;
            };

            _ = _graph.StartPeriodicRefreshAsync();

        }

        [TestCleanup]
        public void Cleanup()
        {
            if (_testException != null)
            {
                Assert.Fail(_testException.Message);
            }
            _graph.StopPeriodicRefresh();
            Console.WriteLine(_graph!.Counters.Dump());


        }

        [TestMethod]
        public void MqttConnectTest()
        {
            var mqClient = _graph!.GetNode("mqc1", NodeType.Client) as IMqttClient;

            Assert.IsNotNull(mqClient);

            mqClient.Connect("mqs1");

            Assert.IsTrue(mqClient.IsConnected);
        }

        [TestMethod]
        public void MqttConnectAndSubscribeTest()
        {
            var mqClient2 = _graph!.GetNode("mqc2", NodeType.Client) as IMqttClient;

            Assert.IsNotNull(mqClient2);


            mqClient2.Connect("mqs1");

            Assert.IsTrue(mqClient2.IsConnected);

            mqClient2.Subscribe("/test/#", MqttQos.AtLeastOnce);

            _graph.Refresh(false);
        }

        [TestMethod]
        public void MqttConnectSubscribeAndPublishTest()
        {
            var mqClient1 = _graph!.GetNode("mqc1", NodeType.Client) as IMqttClient;
            var mqClient2 = _graph!.GetNode("mqc2", NodeType.Client) as IMqttClient;



            Assert.IsNotNull(mqClient1);
            Assert.IsNotNull(mqClient2);


            mqClient1.Connect("mqs1");
            mqClient2.Connect("mqs1");

            Assert.IsTrue(mqClient1.IsConnected);
            Assert.IsTrue(mqClient2.IsConnected);

            mqClient2.Subscribe("/test/#", MqttQos.AtLeastOnce);

            mqClient1.Publish("/test/data1", new byte[] { 1, 2, 3, 4, 5 }, MqttQos.AtLeastOnce);

            mqClient2.MessageReceived += (object? sender, MqttMessage e) =>
            {
                CollectionAssert.AreEqual(e.Payload, new byte[] { 1, 2, 3, 4, 5 });
            };

            Thread.Sleep(1000);
        }
    }
}