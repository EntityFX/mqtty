using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Engines.Execution;
using EntityFX.MqttY.Web.App.Models;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Реализация преобразований конфигураций (см. IConfigurationMapperService).</summary>
public sealed class ConfigurationMapperService : IConfigurationMapperService
{
    public GraphDto ToGraphDto(NetworkGraphOption option)
    {
        var graph = new GraphDto { RandomSeed = option.RandomSeed, Ticks = option.Ticks, NetworkTypes = option.NetworkTypes };

        foreach (var (name, network) in option.Networks)
        {
            graph.Networks.Add(new NetworkDto
            {
                Id = name,
                Name = name,
                NetworkType = network.NetworkType
            });
        }

        foreach (var (name, node) in option.Nodes)
        {
            graph.Nodes.Add(new NodeDto
            {
                Id = name,
                Name = name,
                Type = node.Type.ToString(),
                Specification = node.Specification ?? string.Empty,
                Network = node.Network,
                Broker = node.Broker
                ,ConnectsToServer = node.ConnectsToServer, Configuration = node.Configuration, Additional = node.Additional
            });
        }

        foreach (var (name, network) in option.Networks)
        {
            foreach (var link in network.Links)
            {
                graph.Links.Add(new LinkDto
                {
                    From = name,
                    To = link.Network ?? string.Empty,
                    Weight = link.W
                });
            }
        }

        return graph;
    }

    public NetworkGraphOption ToNetworkGraphOption(GraphDto graph)
    {
        var option = new NetworkGraphOption
        {
            RandomSeed = graph.RandomSeed,
            EnableCounters = true,
            Ticks = new TicksOptions
            {
                TickPeriod = TimeSpan.FromMilliseconds(0.1),
                ReceiveWaitPeriod = TimeSpan.FromSeconds(1),
                OutgoingWaitTicks = 1,
                CounterHistoryDepth = 100
            }
        };
        if (graph.Ticks != null) option.Ticks = graph.Ticks;

        var index = 0;
        foreach (var network in graph.Networks)
        {
            option.Networks[network.Id] = new NetworkNodeOption
            {
                Index = index++,
                NetworkType = network.NetworkType
            };
            option.NetworkTypes[network.NetworkType] = graph.NetworkTypes.GetValueOrDefault(network.NetworkType) ?? new NetworkOptions
            {
                NetworkType = network.NetworkType,
                CapacityBytesPerSecond = 100000000,
                ThroughputWindowTicks = 100,
                QueueCapacity = 100000,
                TransferTicks = 1
            };
        }

        index = 0;
        foreach (var node in graph.Nodes)
        {
            option.Nodes[node.Id] = new NodeOption
            {
                Type = Enum.TryParse<NodeOptionType>(node.Type, out var type) ? type : NodeOptionType.Client,
                Protocol = "mqtt",
                Specification = node.Specification,
                Network = node.Network,
                Broker = node.Broker,
                ConnectsToServer = node.ConnectsToServer, Configuration = node.Configuration, Additional = node.Additional,
                Index = index++
            };
        }

        foreach (var link in graph.Links)
        {
            if (!option.Networks.TryGetValue(link.From, out var network))
            {
                continue;
            }

            var links = network.Links.ToList();
            links.Add(new NetworkLinkOption { Network = link.To, W = link.Weight });
            network.Links = links.ToArray();
        }

        return option;
    }

    public SimulationRunOptions ToRunOptions(NetworkGraphOption graph, string engine, TopologySpecDto spec)
    {
        return new SimulationRunOptions
        {
            Graph = graph,
            Engine = engine,
            WarmupTicks = spec.WarmupTicks,
            MeasurementTicks = spec.MeasurementTicks,
            DrainTimeoutTicks = spec.DrainTimeoutTicks,
            Parallel = false,
            RefreshStrategy = 0
        };
    }
}
