using System.Xml.Linq;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Design;

/// <summary>Pure GraphML → DesignDocument reader without Avalonia/ReactiveUI.</summary>
public static class GraphMlDesignReader
{
    public static DesignDocument Read(string graphml)
    {
        var document = XDocument.Parse(graphml);
        var root = document.Root ?? throw new InvalidDataException("Invalid GraphML: missing root.");
        var ns = root.Name.Namespace;
        var graph = root.Element(ns + "graph")
            ?? throw new InvalidDataException("Invalid GraphML: missing <graph>.");

        var keys = root.Elements(ns + "key")
            .Select(element => new
            {
                Id = element.Attribute("id")?.Value ?? string.Empty,
                Name = element.Attribute("attr.name")?.Value ?? string.Empty
            })
            .Where(key => key.Id.Length > 0)
            .ToDictionary(key => key.Id, key => key.Name, StringComparer.Ordinal);

        string Data(XElement element, string name, string fallback = "")
        {
            var key = keys.FirstOrDefault(pair => pair.Value == name).Key;
            if (key == null) return fallback;
            return element.Elements(ns + "data")
                .FirstOrDefault(data => data.Attribute("key")?.Value == key)?.Value ?? fallback;
        }

        var networkIds = new Dictionary<string, DesignNetworkBuilder>(StringComparer.Ordinal);
        var nodeIds = new Dictionary<string, DesignNodeBuilder>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var element in graph.Elements(ns + "node"))
        {
            var id = element.Attribute("id")?.Value ?? string.Empty;
            if (id.Length == 0) continue;
            var label = Data(element, "label", id);
            var type = Data(element, "type", string.Empty).ToLowerInvariant();
            order.Add(id);

            if (type is "network" or "n" || type.Length == 0 && id.StartsWith("n", StringComparison.OrdinalIgnoreCase))
            {
                networkIds[id] = new DesignNetworkBuilder
                {
                    Name = label,
                    Index = networkIds.Count,
                    NetworkType = Data(element, "networkType", "1g")
                };
                continue;
            }

            var nodeType = type switch
            {
                "server" or "s" => NodeOptionType.Server,
                "application" or "app" or "a" => NodeOptionType.Application,
                _ => id.StartsWith("s", StringComparison.OrdinalIgnoreCase)
                    ? NodeOptionType.Server
                    : id.StartsWith("a", StringComparison.OrdinalIgnoreCase)
                        ? NodeOptionType.Application
                        : NodeOptionType.Client
            };

            var defaultSpecification = nodeType switch
            {
                NodeOptionType.Server => "mqtt-broker",
                NodeOptionType.Application => "mqtt-relay",
                _ => "mqtt-client"
            };

            nodeIds[id] = new DesignNodeBuilder
            {
                Name = label,
                Type = nodeType,
                Protocol = Data(element, "protocol", "mqtt"),
                Specification = Data(element, "specification", defaultSpecification),
                Network = Data(element, "network", string.Empty),
                ConnectsToServer = Data(element, "connectsTo", string.Empty),
                Broker = Data(element, "broker", null!)
            };
        }

        foreach (var edge in graph.Elements(ns + "edge"))
        {
            var source = edge.Attribute("source")?.Value ?? string.Empty;
            var target = edge.Attribute("target")?.Value ?? string.Empty;
            var weight = int.TryParse(Data(edge, "weight", string.Empty), out var parsed) ? parsed : (int?)null;

            if (networkIds.TryGetValue(source, out var from) && networkIds.TryGetValue(target, out var to))
            {
                from.Links.Add(new DesignLink(to.Name, weight));
            }
            else if (nodeIds.TryGetValue(source, out var node) && networkIds.TryGetValue(target, out var network))
            {
                node.Network = network.Name;
            }
        }

        var networks = order.Where(networkIds.ContainsKey).Select(id => networkIds[id]).ToArray();
        var nodes = order.Where(nodeIds.ContainsKey).Select(id => nodeIds[id]).ToArray();
        var typeNames = networks.Select(network => network.NetworkType).Distinct(StringComparer.Ordinal).ToArray();
        if (typeNames.Length == 0) typeNames = new[] { "1g" };

        return new DesignDocument(
            networks.Select(network => new DesignNetwork(network.Name, network.Index,
                network.NetworkType, network.Links.ToArray())).ToArray(),
            nodes.Select(node => new DesignNode(node.Name, node.Type, node.Protocol,
                node.Specification, NullIfEmpty(node.Network), NullIfEmpty(node.ConnectsToServer),
                null, null, NullIfEmpty(node.Broker), null,
                new Dictionary<string, string[]>())).ToArray(),
            typeNames.Select(name => new DesignNetworkType(name, SpeedFor(name), 3, 50000)).ToArray(),
            new TicksOptions
            {
                TickPeriod = TimeSpan.FromMilliseconds(100),
                ReceiveWaitPeriod = TimeSpan.FromMilliseconds(50),
                OutgoingWaitTicks = 5,
                CounterHistoryDepth = 100
            },
            EnableCounters: true,
            RandomSeed: 0);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int SpeedFor(string type) => type switch
    {
        "10g" => 1_250_000_000,
        "wifi5" => 12_500_000,
        _ => 125_000_000
    };

    private sealed class DesignNetworkBuilder
    {
        public string Name { get; init; } = string.Empty;
        public int Index { get; init; }
        public string NetworkType { get; init; } = "1g";
        public List<DesignLink> Links { get; } = new();
    }

    private sealed class DesignNodeBuilder
    {
        public string Name { get; init; } = string.Empty;
        public NodeOptionType Type { get; init; }
        public string Protocol { get; init; } = string.Empty;
        public string Specification { get; init; } = string.Empty;
        public string Network { get; set; } = string.Empty;
        public string ConnectsToServer { get; init; } = string.Empty;
        public string? Broker { get; init; }
    }
}
