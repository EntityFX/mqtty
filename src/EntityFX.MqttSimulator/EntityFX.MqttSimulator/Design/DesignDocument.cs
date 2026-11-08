using System.Text.Json;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Results;

namespace EntityFX.MqttY.Design;

public sealed record DesignLink(string TargetNetwork, int? Weight);

public sealed record DesignNetwork(string Name, int Index, string NetworkType,
    IReadOnlyList<DesignLink> Links);

public sealed record DesignNode(string Name, NodeOptionType Type, string? Protocol,
    string? Specification, string? Network, string? ConnectsToServer, int? Quantity, int? Index,
    string? Broker, object? Configuration, IReadOnlyDictionary<string, string[]> Additional,
    int SendTicks = 0);

public sealed record DesignNetworkType(string Name, int Speed, int TransferTicks, int QueueCapacity);

/// <summary>
/// UI-framework-free design document. Avalonia/ReactiveUI models map to this
/// POCO, and the same document can be exported to the simulator options.
/// </summary>
public sealed record DesignDocument(
    IReadOnlyList<DesignNetwork> Networks,
    IReadOnlyList<DesignNode> Nodes,
    IReadOnlyList<DesignNetworkType> NetworkTypes,
    TicksOptions Ticks,
    bool EnableCounters,
    int RandomSeed,
    GraphLayoutOption? Layout = null)
{
    public static DesignDocument FromNetworkGraphOption(NetworkGraphOption option) => new(
        option.Networks.Select(pair => new DesignNetwork(pair.Key, pair.Value.Index,
            pair.Value.NetworkType,
            pair.Value.Links.Select(link => new DesignLink(link.Network, link.W)).ToArray())).ToArray(),
        option.Nodes.Select(pair => new DesignNode(pair.Key, pair.Value.Type, pair.Value.Protocol,
            pair.Value.Specification, pair.Value.Network, pair.Value.ConnectsToServer,
            pair.Value.Quantity, pair.Value.Index, pair.Value.Broker, pair.Value.Configuration,
            new Dictionary<string, string[]>(pair.Value.Additional), pair.Value.SendTicks)).ToArray(),
        option.NetworkTypes.Select(pair => new DesignNetworkType(pair.Key, pair.Value.Speed,
            pair.Value.TransferTicks, pair.Value.QueueCapacity)).ToArray(),
        option.Ticks, option.EnableCounters, option.RandomSeed, option.Layout);

    public NetworkGraphOption ToNetworkGraphOption()
    {
        var option = new NetworkGraphOption
        {
            Ticks = Ticks,
            EnableCounters = EnableCounters,
            RandomSeed = RandomSeed,
            Layout = Layout
        };

        foreach (var type in NetworkTypes)
        {
            option.NetworkTypes[type.Name] = new NetworkOptions
            {
                Speed = type.Speed,
                TransferTicks = type.TransferTicks,
                QueueCapacity = type.QueueCapacity,
                NetworkType = type.Name
            };
        }

        foreach (var network in Networks)
        {
            option.Networks[network.Name] = new NetworkNodeOption
            {
                Index = network.Index,
                NetworkType = network.NetworkType,
                Links = network.Links.Select(link => new NetworkLinkOption
                {
                    Network = link.TargetNetwork,
                    W = link.Weight
                }).ToArray()
            };
        }

        foreach (var node in Nodes)
        {
            option.Nodes[node.Name] = new NodeOption
            {
                Type = node.Type,
                Protocol = node.Protocol,
                Specification = node.Specification,
                Network = node.Network,
                ConnectsToServer = node.ConnectsToServer,
                Quantity = node.Quantity,
                Index = node.Index,
                Broker = node.Broker,
                Configuration = node.Configuration,
                Additional = new Dictionary<string, string[]>(node.Additional),
                SendTicks = node.SendTicks
            };
        }

        return option;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonPolicies.Result);

    public static DesignDocument FromJson(string json) =>
        JsonSerializer.Deserialize<DesignDocument>(json, JsonPolicies.Result)
        ?? throw new InvalidDataException("Empty design document.");
}
