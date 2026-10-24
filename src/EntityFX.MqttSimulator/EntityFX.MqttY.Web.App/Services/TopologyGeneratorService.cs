using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;
using EntityFX.MqttY.Web.App.Models;
using static EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt.MqttRelayConfiguration;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>
/// Генерирует NetworkGraphOption по параметрам: линейная, кольцо, звезда, комбинированная.
/// Добавляет брокеров, клиентов и узлы движка (relay/federation/load-balancer).
/// </summary>
public sealed class TopologyGeneratorService : ITopologyGeneratorService
{
    private readonly IPresetCatalogService? _catalog;

    public TopologyGeneratorService() : this(null) { }

    public TopologyGeneratorService(IPresetCatalogService? catalog)
    {
        _catalog = catalog;
    }

    public NetworkGraphOption Generate(TopologySpecDto spec)
    {
        Validate(spec);

        var graph = new NetworkGraphOption
        {
            EnableCounters = true,
            RandomSeed = spec.Seed,
            Ticks = new TicksOptions
            {
                TickPeriod = TimeSpan.FromMilliseconds(0.1),
                ReceiveWaitPeriod = TimeSpan.FromSeconds(1),
                OutgoingWaitTicks = 1,
                CounterHistoryDepth = 100
            }
        };

        var networkType = new NetworkOptions
        {
            NetworkType = spec.NetworkType.Name,
            CapacityBytesPerSecond = spec.NetworkType.CapacityBytesPerSecond,
            ThroughputWindowTicks = spec.NetworkType.ThroughputWindowTicks,
            QueueCapacity = (int)spec.NetworkType.QueueCapacity,
            TransferTicks = spec.NetworkType.TransferTicks
        };
        graph.NetworkTypes[spec.NetworkType.Name] = networkType;

        var brokerNetworks = spec.Kind switch
        {
            "line" => BuildLine(graph, spec),
            "ring" => BuildRing(graph, spec),
            "star" => BuildStar(graph, spec),
            "combined" => BuildCombined(graph, spec),
            _ => throw new ArgumentException($"Unknown topology kind '{spec.Kind}'.")
        };

        AddBrokersAndClients(graph, spec, brokerNetworks);

        switch (spec.Engine)
        {
            case "relay":
                AddRelayApps(graph, brokerNetworks, spec);
                break;
            case "federation":
                AddRelayApps(graph, brokerNetworks, spec, federationPrefix: true);
                break;
            case "loadBalancing":
                AddLoadBalancer(graph, brokerNetworks, spec);
                break;
            default:
                throw new ArgumentException($"Unknown engine '{spec.Engine}'.");
        }

        return graph;
    }

    private static void Validate(TopologySpecDto spec)
    {
        if (spec.Kind is "line" or "combined" && spec.ChainLength < 2)
        {
            throw new ArgumentException("ChainLength must be at least 2.");
        }

        if (spec.Kind is "ring" && spec.RingLength < 3)
        {
            throw new ArgumentException("RingLength must be at least 3.");
        }

        if (spec.Kind is "star" or "combined" && spec.Rays < 1)
        {
            throw new ArgumentException("Rays must be at least 1.");
        }
    }

    private static List<string> BuildLine(NetworkGraphOption graph, TopologySpecDto spec)
    {
        var count = spec.Brokers ?? spec.ChainLength;
        var networks = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var name = $"net-{i}";
            AddNetwork(graph, name, i, spec.NetworkType.Name);
            networks.Add(name);
        }

        for (var i = 0; i < count - 1; i++)
        {
            AddLink(graph, networks[i], networks[i + 1]);
        }

        return networks;
    }

    private static List<string> BuildRing(NetworkGraphOption graph, TopologySpecDto spec)
    {
        var count = spec.Brokers ?? spec.RingLength;
        var networks = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var name = $"net-{i}";
            AddNetwork(graph, name, i, spec.NetworkType.Name);
            networks.Add(name);
        }

        for (var i = 0; i < count; i++)
        {
            AddLink(graph, networks[i], networks[(i + 1) % count]);
        }

        return networks;
    }

    private static List<string> BuildStar(NetworkGraphOption graph, TopologySpecDto spec)
    {
        var networks = new List<string>();
        AddNetwork(graph, "net-core", 0, spec.NetworkType.Name);

        for (var r = 0; r < spec.Rays; r++)
        {
            var previous = "net-core";
            for (var s = 0; s < spec.ChainLength; s++)
            {
                var name = $"net-r{r}-s{s}";
                AddNetwork(graph, name, networks.Count, spec.NetworkType.Name);
                if (s == spec.ChainLength - 1) networks.Add(name);
                AddLink(graph, previous, name);
                previous = name;
            }
        }

        return networks;
    }

    private static List<string> BuildCombined(NetworkGraphOption graph, TopologySpecDto spec)
    {
        var networks = new List<string>();
        var coreCount = spec.RingLength;
        for (var i = 0; i < coreCount; i++)
        {
            var name = $"net-core-{i}";
            AddNetwork(graph, name, i, spec.NetworkType.Name);
            networks.Add(name);
        }

        for (var i = 0; i < coreCount; i++)
        {
            AddLink(graph, networks[i], networks[(i + 1) % coreCount]);
        }

        for (var r = 0; r < spec.Rays; r++)
        {
            var previous = networks[0];
            for (var s = 0; s < spec.ChainLength; s++)
            {
                var name = $"net-r{r}-s{s}";
                AddNetwork(graph, name, networks.Count, spec.NetworkType.Name);
                networks.Add(name);
                AddLink(graph, previous, name);
                previous = name;
            }
        }

        return networks;
    }

    private void AddBrokersAndClients(NetworkGraphOption graph, TopologySpecDto spec, List<string> brokerNetworks)
    {
        var brokerType = spec.BrokerPresetId;
        if (_catalog != null)
        {
            var preset = _catalog.GetBrokerPresetAsync(spec.BrokerPresetId).GetAwaiter().GetResult();
            if (preset != null && !string.IsNullOrWhiteSpace(preset.BrokerType))
            {
                brokerType = preset.BrokerType;
            }
        }

        var brokerIndex = 0;
        foreach (var network in brokerNetworks)
        {
            var brokerName = $"broker-{brokerIndex}";
            graph.Nodes[brokerName] = new NodeOption
            {
                Type = NodeOptionType.Server,
                Protocol = "mqtt",
                Specification = "mqtt-broker",
                Network = network,
                Broker = brokerType,
                Index = brokerIndex
            };

            for (var c = 0; c < spec.ClientsPerBroker; c++)
            {
                var clientName = $"client-{brokerIndex}-{c}";
                graph.Nodes[clientName] = new NodeOption
                {
                    Type = NodeOptionType.Client,
                    Protocol = "mqtt",
                    Specification = "mqtt-client",
                    Network = network,
                    ConnectsToServer = brokerName,
                    Index = brokerIndex
                };
            }

            brokerIndex++;
        }
    }

    private static void AddRelayApps(NetworkGraphOption graph, List<string> brokerNetworks, TopologySpecDto spec, bool federationPrefix = false)
    {
        // Имена брокеров соответствуют порядку сетей: broker-0..broker-N
        var brokerNames = Enumerable.Range(0, brokerNetworks.Count).Select(i => $"broker-{i}").ToList();

        for (var i = 0; i < brokerNames.Count; i++)
        {
            var targets = brokerNames.Where((_, j) => j != i).ToList();
            if (targets.Count == 0)
            {
                continue;
            }

            var relayName = $"relay-{i}";
            var relayConfig = new MqttRelayConfiguration
            {
                ListenTopics = new Dictionary<string, MqttListenConfigurationItem>
                {
                    ["listen"] = new()
                    {
                        Server = brokerNames[i],
                        Topics = new[] { "telemetry/+" },
                        Qos = MqttQos.AtLeastOnce
                    }
                },
                RelayTopics = new Dictionary<string, MqttRelayConfigurationItem>(),
                RouteMap = new Dictionary<string, string[]>()
            };

            var routeTargets = new List<string>();
            foreach (var target in targets)
            {
                var key = $"relay-{target}";
                relayConfig.RelayTopics[key] = new MqttRelayConfigurationItem
                {
                    Server = target,
                    TopicPrefix = federationPrefix ? $"fed{target}/" : $"relay{target}/",
                    ReplaceRelaySegment = true
                };
                routeTargets.Add(key);
            }

            relayConfig.RouteMap["listen"] = routeTargets.ToArray();

            graph.Nodes[relayName] = new NodeOption
            {
                Type = NodeOptionType.Application,
                Protocol = "mqtt",
                Specification = "mqtt-relay",
                Network = brokerNetworks[i],
                Configuration = relayConfig,
                Index = i
            };
        }
    }

    private static void AddLoadBalancer(NetworkGraphOption graph, List<string> brokerNetworks, TopologySpecDto spec)
    {
        graph.Nodes["load-balancer"] = new NodeOption
        {
            Type = NodeOptionType.Application,
            Protocol = "mqtt",
            Specification = "mqtt-load-balancer",
            Network = brokerNetworks[0],
            Index = 0
        };
    }

    private static void AddNetwork(NetworkGraphOption graph, string name, int index, string networkType)
    {
        graph.Networks[name] = new NetworkNodeOption
        {
            Index = index,
            NetworkType = networkType
        };
    }

    private static void AddLink(NetworkGraphOption graph, string from, string to)
    {
        var links = graph.Networks[from].Links.ToList();
        links.Add(new NetworkLinkOption { Network = to });
        graph.Networks[from].Links = links.ToArray();
    }
}
