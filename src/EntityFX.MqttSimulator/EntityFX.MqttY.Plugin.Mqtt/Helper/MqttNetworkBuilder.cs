using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;
using Microsoft.Extensions.Options;
using System.Net;
using System.Xml.Linq;
using static EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt.MqttRelayConfiguration;

namespace EntityFX.MqttY.Plugin.Mqtt.Helper;

public class MqttNetworkBuilder : NetworkBuilderBase
{
    private readonly IMqttPacketManager mqttPacketManager;
    private readonly IMqttTopicEvaluator mqttTopicEvaluator;
    private readonly IClientBuilder clientBuilder;
    private readonly Func<int, MqttBrokerProfile?>? brokerProfileResolver;
    private readonly int randomSeed;
    private readonly MqttQos subscribeQos;
    private readonly int? profileClientCount;
    private int brokerOrdinal;

    public MqttNetworkBuilder(INetworkSimulator networkSimulator, IMqttPacketManager mqttPacketManager,
        IMqttTopicEvaluator mqttTopicEvaluator, IClientBuilder clientBuilder,
        Func<int, MqttBrokerProfile?>? brokerProfileResolver = null,
        int randomSeed = 0, MqttQos subscribeQos = MqttQos.AtLeastOnce, int? profileClientCount = null)
        : base(networkSimulator)
    {
        this.mqttPacketManager = mqttPacketManager;
        this.mqttTopicEvaluator = mqttTopicEvaluator;
        this.clientBuilder = clientBuilder;
        this.brokerProfileResolver = brokerProfileResolver;
        this.randomSeed = randomSeed;
        this.subscribeQos = subscribeQos;
        this.profileClientCount = profileClientCount;
    }

    protected override IServer CreateServer(TicksOptions ticksOptions, int ix, string name, string fullName, string address)
    {
        var profile = brokerProfileResolver?.Invoke(brokerOrdinal++);
        return new MqttBroker(mqttPacketManager, mqttTopicEvaluator, ix, name, address,
            "mqtt", "mqtt", ticksOptions, networkSimulator.EnableCounters, profile, randomSeed, profileClientCount);
    }

    protected override IClient CreateClient(TicksOptions ticksOptions, int ix, string name, string fullName, string address)
    {
        return new MqttClient(mqttPacketManager, ix, name, address, "mqtt", "mqtt", name, ticksOptions, networkSimulator.EnableCounters);
    }

    protected override IApplication CreateApplication(
        TicksOptions ticksOptions, int ix, string name, string fullName, 
        string address, string specification,
        NetworkBuilderApplicationFunc<object>? appOptionsFunc)
    {
        switch (specification)
        {
            case "mqtt-relay":
                return new MqttRelay(ix, name, address,
                    "mqtt", specification, clientBuilder, mqttTopicEvaluator,
                    ticksOptions,
                    appOptionsFunc?.Invoke(ix, fullName, specification) as MqttRelayConfiguration, networkSimulator.EnableCounters);
            case "mqtt-receiver":
                return new MqttReceiver(clientBuilder, ix, name, address, 
                    "mqtt", specification, ticksOptions,
                    appOptionsFunc?.Invoke(ix, fullName, specification) as MqttReceiverConfiguration, networkSimulator.EnableCounters
                );
        }

        return new Application<string>(ix, fullName, address, "mqtt", specification, ticksOptions, null, networkSimulator.EnableCounters);
    }

    public IMqttBroker[] BuildMqttRelay(NetworkSimulator graph, TicksOptions ticksOptions)
    {
        var brokers = graph.Servers.Values.OfType<IMqttBroker>().ToArray();

        foreach (var broker in brokers!)
        {
            var oppositeBrokers = brokers.Where(b => b.Name != broker.Name);


            var brokerNetwork = broker.Network;

            CreateRelay(ticksOptions, broker, oppositeBrokers);

            CreateReceiver(ticksOptions, broker, clientBuilder);

        }

        return brokers;
    }

    public void BuildBrokerFidelity(NetworkSimulator graph, TicksOptions ticksOptions)
    {
        foreach (var broker in graph.Servers.Values.OfType<IMqttBroker>())
            CreateReceiver(ticksOptions, broker, clientBuilder);
    }

    private int CreateReceiver(TicksOptions ticksOptions, IMqttBroker broker, IClientBuilder clientBuilder)
    {
        var brokerNetwork = broker.Network!;
        var rix = broker!.Index;
        var receiverName = $"mqrc{rix}";
        var receiverAddress = $"mqtt://{receiverName}";

        var graph = broker.NetworkSimulator;
        var ix = graph!.CountNodes + 1;


        var receiverConfiguration = new MqttReceiverConfiguration()
        {
            Server = broker.Name,
            Qos = subscribeQos,
            Topics = new string[] {
                            "telemetry/+",
                            "local/telemetry/+"
                        }
        };

        var receiver = new MqttReceiver(clientBuilder, ix, receiverName, receiverAddress, "mqtt", "mqtt-receiver",
            ticksOptions, receiverConfiguration, networkSimulator.EnableCounters);
        brokerNetwork.AddApplication(receiver);
        graph.AddApplication(receiver);
        return ix;
    }

    private void CreateRelay(TicksOptions ticksOptions, IMqttBroker broker, IEnumerable<IMqttBroker> oppositeBrokers)
    {
        var brokerNetwork = broker.Network!;

        var rix = broker!.Index;
        var relayName = $"mqrl{rix}";
        var address = $"mqtt://{relayName}";

        var graph = broker.NetworkSimulator;
        var ix = graph!.CountNodes + 1;

        var relayRemoteTopics = oppositeBrokers.ToDictionary(k => $"rs{k.Index}",
            v => new MqttRelayConfigurationItem() { ReplaceRelaySegment = false, Server = v.Name, TopicPrefix = $"relay{v.Index}" });

        var lsMap = relayRemoteTopics.Keys.ToArray();

        relayRemoteTopics.Add($"lrs{rix}", new MqttRelayConfigurationItem()
        {
            ReplaceRelaySegment = true,
            Server = broker.Name,
            TopicPrefix = $"local/"
        });


        var rlsMap = new string[] { $"lrs{rix}" };

        var routeMap = new Dictionary<string, string[]>()
        {
            [$"ls{rix}"] = lsMap,
            [$"rls{rix}"] = rlsMap,
        };

        var relayConfiguration = new MqttRelayConfiguration()
        {
            ListenTopics = new Dictionary<string, MqttRelayConfiguration.MqttListenConfigurationItem>()
            {
                [$"ls{rix}"] = new MqttRelayConfiguration.MqttListenConfigurationItem()
                {
                    Server = broker.Name,
                    Qos = subscribeQos,
                    Topics = new string[] { "telemetry/+" }
                },
                [$"rls{rix}"] = new MqttRelayConfiguration.MqttListenConfigurationItem()
                {
                    Server = broker.Name,
                    Qos = subscribeQos,
                    Topics = new string[] { $"relay{rix}/telemetry/+" }
                },
            },
            RelayTopics = relayRemoteTopics,
            RouteMap = routeMap
        };

        var relay = new MqttRelay(ix, relayName, address, "mqtt", "mqtt-relay", clientBuilder,
            mqttTopicEvaluator, ticksOptions, relayConfiguration, networkSimulator.EnableCounters);
        brokerNetwork.AddApplication(relay);
        brokerNetwork.NetworkSimulator!.AddApplication(relay);
    }
}
