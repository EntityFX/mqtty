using System.Text.Json;
using System.Text;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt.Helper;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Utils;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Plugin.Mqtt.Factories;

record InParams(int Brokers, int Nets, int Clients, int Repeats, bool IsParallel, bool EnabledCounters);
record OutParams(TimeSpan VirtualTime, TimeSpan RealTime, long TotalTicks, long TotalSteps, long Errors, double MemoryWorkingSet);
record ResultItem(InParams In, OutParams Out, bool rowLine);

public class MqttRelayApp
{
    private readonly GenericBuilder _builder;

    private readonly TicksOptions _tickOptions;
    private readonly NetworkOptions _networkOptions;

    private INetworkLogger _monitoring;
    //private readonly INetworkLoggerProvider _monitoringProvider;
    private IMqttTopicEvaluator _mqttTopicEvaluator;
    private IMqttPacketManager _mqttPacketManager;



    public MqttRelayApp()
    {
        _builder = new GenericBuilder();

        _tickOptions = _builder.GetDefaultTickOptions();
        _networkOptions = _builder.GetDefaultNetworkOptions();

        //_monitoring = _builder.GetNetworkLogger();
        _monitoring = _builder.GetNullNetworkLogger();

        _mqttTopicEvaluator = _builder.GetMqttTopicEvaluator();
        _mqttPacketManager = _builder.GetMqttPacketManager(_mqttTopicEvaluator);
    }

    private INetworkSimulator BuildNetworkSimulator(int relays, int length, int clients, bool enableCounters)
    {
        var pathFinder = _builder.GetPathFinder();
        var clientBuilder = _builder.GetClientBuilder(AppBuild);

        var graph = new NetworkSimulator(pathFinder, _monitoring!, _tickOptions, enableCounters);
        var networkBuilder = new MqttNetworkBuilder(graph!, _mqttPacketManager, _mqttTopicEvaluator, clientBuilder);
        graph!.Construction = true;

        var networks = networkBuilder.BuildSimpleTree(relays, length, clients, 1, null, true, _tickOptions, _networkOptions);

        var brokers = networkBuilder.BuildMqttRelay(graph, _tickOptions);

        graph.Construction = false;
        graph.UpdateRoutes();

        return graph;

    }

    public INetworkSimulator ExecuteSimulation(bool isParallel, int relays, int length, int clients, int sendRepeats, bool enableCounters)
    {
        var graph = BuildNetworkSimulator(relays, length, clients, enableCounters);
        var brokers = graph.Servers.Values.OfType<IMqttBroker>().ToArray();

        var mqttRelays = ConnectMqttRelayApps(graph);
        var mqttReceivers = ConnectMqttReceiverApps(graph);

        ConnectMqttClientsToBrokers(brokers);

        var allConnected = RefreshUntilConnected(isParallel, graph);

        var ticks = graph.TotalTicks;

        SubscribeAllRelays(mqttRelays);

        RefreshTicks(isParallel, graph, ticks);

        SubscribeAllMqttReceivers(mqttReceivers);

        RefreshTicks(isParallel, graph, ticks);


        var plantUmlGraphGenerator = new SimpleGraphMlGenerator();
        var uml = plantUmlGraphGenerator.SerializeNetworkGraph(graph!);


        ///TEST Relay subscriptions
        var (relayWithSubscription, relaysWithoutSubscription) = VerifyAllMqttRelaysSubscribed(mqttRelays);
        var (countRelaySubscribed, withoutSubsciption) = VerifyAllMqttReceiversSubscibed(mqttReceivers);

        var data = new { Temperature = 25.0, Hummidity = 50.0, Pressure = 720.0 };
        var dataJson = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(dataJson);

        for (int r = 0; r < sendRepeats; r++)
        {
            foreach (var broker in brokers)
            {
                var brokerNetwork = broker.Network!;
                var mqttClients = brokerNetwork.Clients.Values.OfType<MqttClient>().Where(c => c.Group == null);

                foreach (var mqttClient in mqttClients)
                {
                    mqttClient.Publish("telemetry/data", bytes, MqttQos.AtLeastOnce, false);
                }
            }

            RefreshTicks(isParallel, graph, ticks);
            RefreshTicks(isParallel, graph, ticks);


        }

        return graph;
    }

    private static (int Subscribed, int WithoutSubscription) VerifyAllMqttReceiversSubscibed(IEnumerable<MqttReceiver> mqttReceivers)
    {
        var subscribed = 0;
        var withoutSubscription = 0;

        foreach (var mqttReceiver in mqttReceivers)
        {
            var options = mqttReceiver.Options!;

            foreach (var listenTopic in options.Topics)
            {

                var hasSubscribtion = mqttReceiver.HasListenSubscription(options.Server, listenTopic);

                if (!hasSubscribtion)
                {
                    //Assert.Fail($"Missing subsciption for server {options.Server} and topic {listenTopic}");
                    withoutSubscription++;
                }

                subscribed++;
            }
        }

        return (subscribed, withoutSubscription);
    }

    private static (int Subscribed, int WithoutSubscription)  VerifyAllMqttRelaysSubscribed(IEnumerable<MqttRelay> mqttRelays)
    {
        var countRelaySubscribed = 0;
        var withoutSubscriptionCount = 0;

        foreach (var mqttRelay in mqttRelays)
        {
            var options = mqttRelay.Options!;

            foreach (var listenTopic in options.ListenTopics)
            {
                foreach (var topic in listenTopic.Value.Topics)
                {
                    var hasSubscribtion = mqttRelay.HasListenSubscription(listenTopic.Key, listenTopic.Value.Server, topic);

                    if (!hasSubscribtion)
                    {
                        withoutSubscriptionCount++;
                        //Assert.Fail($"Missing subsciption {listenTopic.Key} for server {listenTopic.Value.Server} and topic {topic}");
                    }

                    countRelaySubscribed++;
                }
            }
        }

        return (countRelaySubscribed, withoutSubscriptionCount);
    }

    private static void SubscribeAllMqttReceivers(IEnumerable<MqttReceiver> mqttReceivers)
    {
        foreach (var mqttReceiver in mqttReceivers)
        {
            mqttReceiver.SubscribeAll();
        }
    }

    private static void SubscribeAllRelays(IEnumerable<MqttRelay> mqttRelays)
    {
        foreach (var mqttRelay in mqttRelays)
        {
            mqttRelay.SubscribeAll();
        }
    }

    private static void ConnectMqttClientsToBrokers(IMqttBroker[] brokers)
    {
        foreach (var broker in brokers)
        {
            var brokerNetwork = broker.Network!;
            var mqttClients = brokerNetwork.Clients.Values.OfType<MqttClient>().ToArray();
            foreach (var mqttClient in mqttClients)
            {
                mqttClient.BeginConnect(broker.Name);
            }
        }
    }

    private static IEnumerable<MqttReceiver> ConnectMqttReceiverApps(INetworkSimulator graph)
    {
        var mqttReceivers = graph.Applications.Values.OfType<MqttReceiver>();
        foreach (var mqttReceiver in mqttReceivers)
        {
            mqttReceiver!.Start();
        }

        return mqttReceivers;
    }

    private static IEnumerable<MqttRelay> ConnectMqttRelayApps(INetworkSimulator graph)
    {
        var mqttRelays = graph.Applications.Values.OfType<MqttRelay>();
        foreach (var mqttRelay in mqttRelays)
        {
            mqttRelay!.Start();
        }

        return mqttRelays;
    }

    private static bool RefreshUntilConnected(bool isParallel, INetworkSimulator graph)
    {
        var allConnected = false;

        while (!allConnected)
        {
            allConnected = graph!.Clients.All(c => c.Value.IsConnected);
            //var nonConnected = _graph!.Clients.Where(c => !c.Value.IsConnected);
            graph.RefreshWithCounters(isParallel);
        }

        return allConnected;
    }

    private void RefreshTicks(bool isParallel, INetworkSimulator graph, long ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            graph.RefreshWithCounters(isParallel);
        }
    }

    private IClient AppBuild(
        int index, string name, string protocolType, string specification, INetwork network, 
        TicksOptions ticks, bool enableCounters, string? group, int? groupAmount, Dictionary<string, string[]>? additional)
    {
        var address = $"mqtt://{name}";
        var clientName = name.Replace(".", "");

        var mqttClient = new MqttClient(_mqttPacketManager, index,
        name, address,
        protocolType, specification, clientName, ticks, enableCounters)
        {
            Group = group,
            GroupAmount = groupAmount
        };

        //options.Network, options.NetworkGraph,

        network!.AddClient(mqttClient);
        network.NetworkSimulator!.AddClient(mqttClient);

        return mqttClient;
    }
}

