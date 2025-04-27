using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Mqtt.Internals;
using EntityFX.MqttY.Mqtt.Internals.Formatters;
using EntityFX.MqttY.Network;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;

namespace EntityFX.Tests.Integration
{

    [TestClass]
    public class MqttTests
    {
        private ServiceProvider? serviceProvider;
        private NetworkLogger? monitoring;
        private ConsoleNetworkLoggerProvider? monitoringProvider;
        private NetworkGraph? graph;

        private Exception? testException;

        [TestInitialize]
        public void Initialize()
        {
            var serviceCollection = new ServiceCollection()
            .AddScoped<IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>, ClientFactory>()
            .AddScoped<IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>, ServerFactory>()
            .AddScoped<IFactory<INetwork, NodeBuildOptions<NetworkBuildOption>>, NetworkFactory>()
            .AddScoped<IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>, GenericApplicationFactiory>()
            .AddScoped<IMqttPacketManager, MqttNativePacketManager>()
            //.AddScoped<IMqttPacketManager, MqttJsonPacketManager>()
            .AddScoped<IMqttTopicEvaluator, MqttTopicEvaluator>((serviceProvider) => new MqttTopicEvaluator(true))
            .AddScoped<INetworkBuilder, NetworkBuilder>()
            .AddScoped<INetworkSimulatorBuilder, NetworkSimulatorBuilder>();

            serviceProvider = serviceCollection.BuildServiceProvider();


            monitoring = new NetworkLogger(false, TimeSpan.FromMilliseconds(0.1), new MonitoringIgnoreOption()
            {
                Category = new string[] { "Refresh" }
            });
            monitoringProvider = new ConsoleNetworkLoggerProvider(monitoring);

            monitoringProvider.Start();

            var networkBuilder = serviceProvider?.GetRequiredService<INetworkBuilder>();
            var networkSimulatorBuilder = serviceProvider?.GetRequiredService<INetworkSimulatorBuilder>();

            graph = new NetworkGraph(serviceProvider!, networkBuilder!, new DijkstraPathFinder(), monitoring!, 
                new TicksOptions() {  
                    ReceiveWaitPeriod = TimeSpan.FromMilliseconds(0.1)});

            networkSimulatorBuilder!.Configure(graph, new NetworkGraphOption()
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

            graph!.OnError += (sender, e) =>
            {
                testException = e;
            };

            _ = graph.StartPeriodicRefreshAsync();

        }

        [TestCleanup]
        public void Cleanup()
        {
            if (testException != null)
            {
                Assert.Fail(testException.Message);
            }

            Console.WriteLine(graph!.Counters.Dump());
        }

        [TestMethod]
        public async Task MqttConnectTest()
        {
            var mqClient = graph!.GetNode("mqc1", NodeType.Client) as IMqttClient;

            Assert.IsNotNull(mqClient);

            await mqClient.ConnectAsync("mqs1");

            Assert.IsTrue(mqClient.IsConnected);
        }

        [TestMethod]
        public async Task MqttConnectAndSubscribeTest()
        {
            var mqClient2 = graph!.GetNode("mqc2", NodeType.Client) as IMqttClient;

            Assert.IsNotNull(mqClient2);


            await mqClient2.ConnectAsync("mqs1");

            Assert.IsTrue(mqClient2.IsConnected);

            await mqClient2.SubscribeAsync("/test/#", MqttQos.AtLeastOnce);

            graph.Refresh();
        }

        [TestMethod]
        public async Task MqttConnectSubscribeAndPublishTest()
        {
            var mqClient1 = graph!.GetNode("mqc1", NodeType.Client) as IMqttClient;
            var mqClient2 = graph!.GetNode("mqc2", NodeType.Client) as IMqttClient;



            Assert.IsNotNull(mqClient1);
            Assert.IsNotNull(mqClient2);


            await mqClient1.ConnectAsync("mqs1");
            await mqClient2.ConnectAsync("mqs1");

            Assert.IsTrue(mqClient1.IsConnected);
            Assert.IsTrue(mqClient2.IsConnected);

            await mqClient2.SubscribeAsync("/test/#", MqttQos.AtLeastOnce);

            await mqClient1.PublishAsync("/test/data1", new byte[] { 1, 2, 3, 4, 5 }, MqttQos.AtLeastOnce);

            mqClient2.MessageReceived += (object? sender, MqttMessage e) =>
            {
                CollectionAssert.AreEqual(e.Payload, new byte[] { 1, 2, 3, 4, 5 });
            };

            await Task.Delay(1000);
        }
    }
}