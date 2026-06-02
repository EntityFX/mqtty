using System;
using System.Globalization;
using System.Xml.Linq;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Designer.Models;

namespace EntityFX.MqttY.Designer.Services;

/// <summary>
/// Imports a network design model from GraphML format.
/// Parses nodes (networks, clients, servers, applications) and edges (links).
/// </summary>
public class GraphMLImporterService
{
    private static readonly XNamespace Ns = XNamespace.Get("http://graphml.graphdrawing.org/xmlns");

    /// <summary>
    /// Imports a NetworkDesignModel from a GraphML file.
    /// </summary>
    public async Task<NetworkDesignModel> ImportFromFileAsync(string path)
    {
        var content = await File.ReadAllTextAsync(path);
        return ImportFromString(content);
    }

    /// <summary>
    /// Imports a NetworkDesignModel from a GraphML string.
    /// </summary>
    public NetworkDesignModel ImportFromString(string graphmlContent)
    {
        var doc = XDocument.Parse(graphmlContent);
        var graphmlEl = doc.Root;
        if (graphmlEl == null)
            throw new InvalidOperationException("Invalid GraphML: root element is missing.");

        var graphEl = graphmlEl.Element(Ns + "graph");
        if (graphEl == null)
            throw new InvalidOperationException("Invalid GraphML: <graph> element is missing.");

        // Build a lookup of key definitions: id -> (attr.name, attr.type, for)
        var keyDefs = new Dictionary<string, (string Name, string Type, string For)>();
        foreach (var keyEl in graphmlEl.Elements(Ns + "key"))
        {
            var id = keyEl.Attribute("id")?.Value ?? "";
            var name = keyEl.Attribute("attr.name")?.Value ?? "";
            var type = keyEl.Attribute("attr.type")?.Value ?? "string";
            var forEl = keyEl.Attribute("for")?.Value ?? "node";
            keyDefs[id] = (name, type, forEl);
        }

        // Parse all nodes
        var nodeElements = new List<(XElement Element, string Id)>();
        foreach (var nodeEl in graphEl.Elements(Ns + "node"))
        {
            var id = nodeEl.Attribute("id")?.Value ?? "";
            nodeElements.Add((nodeEl, id));
        }

        // Parse all edges
        var edgeElements = new List<(XElement Element, string Source, string Target)>();
        foreach (var edgeEl in graphEl.Elements(Ns + "edge"))
        {
            var source = edgeEl.Attribute("source")?.Value ?? "";
            var target = edgeEl.Attribute("target")?.Value ?? "";
            edgeElements.Add((edgeEl, source, target));
        }

        // Extract data helper
        string GetData(XElement element, string keyId, string defaultValue = "")
        {
            var dataEl = element.Elements(Ns + "data")
                .FirstOrDefault(d => d.Attribute("key")?.Value == keyId);
            return dataEl?.Value ?? defaultValue;
        }

        string GetDataByName(XElement element, string attrName, string defaultValue = "")
        {
            // Find the key id that has this attr.name
            var keyId = keyDefs
                .Where(kv => kv.Value.Name == attrName)
                .Select(kv => kv.Key)
                .FirstOrDefault();
            if (keyId == null) return defaultValue;
            return GetData(element, keyId, defaultValue);
        }

        // Classify nodes by type
        var networkNodes = new List<(string Id, XElement El, string Name)>();
        var clientNodes = new List<(string Id, XElement El, string Name)>();
        var serverNodes = new List<(string Id, XElement El, string Name)>();
        var appNodes = new List<(string Id, XElement El, string Name)>();

        foreach (var (el, id) in nodeElements)
        {
            var typeStr = GetDataByName(el, "type", "");
            var name = GetDataByName(el, "label", id);

            switch (typeStr.ToLowerInvariant())
            {
                case "network":
                case "n":
                    networkNodes.Add((id, el, name));
                    break;
                case "client":
                case "c":
                    clientNodes.Add((id, el, name));
                    break;
                case "server":
                case "s":
                    serverNodes.Add((id, el, name));
                    break;
                case "application":
                case "app":
                case "a":
                    appNodes.Add((id, el, name));
                    break;
                default:
                    // Try to infer from id prefix
                    if (id.StartsWith("n", StringComparison.OrdinalIgnoreCase))
                        networkNodes.Add((id, el, name));
                    else if (id.StartsWith("c", StringComparison.OrdinalIgnoreCase))
                        clientNodes.Add((id, el, name));
                    else if (id.StartsWith("s", StringComparison.OrdinalIgnoreCase))
                        serverNodes.Add((id, el, name));
                    else if (id.StartsWith("a", StringComparison.OrdinalIgnoreCase))
                        appNodes.Add((id, el, name));
                    break;
            }
        }

        // Build the design model
        var model = new NetworkDesignModel
        {
            Ticks = new TicksOptions
            {
                TickPeriod = TimeSpan.FromMilliseconds(100),
                ReceiveWaitPeriod = TimeSpan.FromMilliseconds(50),
                OutgoingWaitTicks = 5,
                CounterHistoryDepth = 100
            },
            EnableCounters = true
        };

        // Collect unique network types from network nodes
        var networkTypeNames = new HashSet<string>();
        foreach (var (_, el, name) in networkNodes)
        {
            var typeName = GetDataByName(el, "networkType", "1g");
            if (!string.IsNullOrEmpty(typeName))
                networkTypeNames.Add(typeName);
        }

        // Create default network types if none found
        if (networkTypeNames.Count == 0)
        {
            networkTypeNames.Add("1g");
        }

        foreach (var typeName in networkTypeNames)
        {
            model.NetworkTypes.Add(new NetworkTypeModel
            {
                Name = typeName,
                Speed = typeName switch
                {
                    "10g" => 1250000000,
                    "wifi5" => 12500000,
                    _ => 125000000
                },
                RefreshTicks = 3,
                SendTicks = 5,
                QueueSize = 50000
            });
        }

        // Build a mapping from node id to network name for edge resolution
        var networkIdToName = new Dictionary<string, string>();
        foreach (var (id, el, name) in networkNodes)
        {
            networkIdToName[id] = name;
        }

        var clientIdToName = new Dictionary<string, string>();
        foreach (var (id, el, name) in clientNodes)
        {
            clientIdToName[id] = name;
        }
        var serverIdToName = new Dictionary<string, string>();
        foreach (var (id, el, name) in serverNodes)
        {
            serverIdToName[id] = name;
        }
        var appIdToName = new Dictionary<string, string>();
        foreach (var (id, el, name) in appNodes)
        {
            appIdToName[id] = name;
        }

        // Add networks
        int netIndex = 0;
        foreach (var (id, el, name) in networkNodes)
        {
            var typeName = GetDataByName(el, "networkType", model.NetworkTypes.FirstOrDefault()?.Name ?? "1g");
            model.Networks.Add(new NetworkNodeDesignModel
            {
                Name = name,
                Index = netIndex++,
                NetworkType = typeName
            });
        }

        // Add clients
        foreach (var (id, el, name) in clientNodes)
        {
            var address = GetDataByName(el, "address", "");
            var connectsTo = GetDataByName(el, "connectsTo", "");
            var protocol = GetDataByName(el, "protocol", "mqtt");
            var spec = GetDataByName(el, "specification", "mqtt-client");

            // Find network from edges
            var networkName = ResolveNodeNetwork(id, edgeElements, networkIdToName);

            model.Nodes.Add(new NodeDesignModel
            {
                Name = name,
                Type = NodeOptionType.Client,
                Protocol = protocol,
                Specification = spec,
                Network = networkName ?? model.Networks.FirstOrDefault()?.Name,
                ConnectsToServer = !string.IsNullOrEmpty(connectsTo) ? connectsTo : null,
                Quantity = 1
            });
        }

        // Add servers
        foreach (var (id, el, name) in serverNodes)
        {
            var address = GetDataByName(el, "address", "");
            var protocol = GetDataByName(el, "protocol", "mqtt");
            var spec = GetDataByName(el, "specification", "mqtt-broker");

            var networkName = ResolveNodeNetwork(id, edgeElements, networkIdToName);

            model.Nodes.Add(new NodeDesignModel
            {
                Name = name,
                Type = NodeOptionType.Server,
                Protocol = protocol,
                Specification = spec,
                Network = networkName ?? model.Networks.FirstOrDefault()?.Name,
                Quantity = 1
            });
        }

        // Add applications
        foreach (var (id, el, name) in appNodes)
        {
            var address = GetDataByName(el, "address", "");
            var protocol = GetDataByName(el, "protocol", "mqtt");
            var spec = GetDataByName(el, "specification", "mqtt-relay");

            var networkName = ResolveNodeNetwork(id, edgeElements, networkIdToName);

            model.Nodes.Add(new NodeDesignModel
            {
                Name = name,
                Type = NodeOptionType.Application,
                Protocol = protocol,
                Specification = spec,
                Network = networkName ?? model.Networks.FirstOrDefault()?.Name,
                Quantity = 1
            });
        }

        // Add links between networks from edges
        foreach (var (el, source, target) in edgeElements)
        {
            if (networkIdToName.ContainsKey(source) && networkIdToName.ContainsKey(target))
            {
                var sourceNet = networkIdToName[source];
                var targetNet = networkIdToName[target];

                var weightStr = GetDataByName(el, "weight", "1");
                int.TryParse(weightStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var weight);

                var sourceModel = model.Networks.FirstOrDefault(n => n.Name == sourceNet);
                if (sourceModel != null)
                {
                    // Avoid duplicate links
                    if (!sourceModel.Links.Any(l => l.TargetNetwork == targetNet))
                    {
                        sourceModel.Links.Add(new LinkDesignModel
                        {
                            TargetNetwork = targetNet,
                            Weight = weight > 0 ? weight : 1
                        });
                    }
                }
            }
        }

        return model;
    }

    /// <summary>
    /// Resolves which network a node belongs to by examining edges.
    /// </summary>
    private static string? ResolveNodeNetwork(
        string nodeId,
        List<(XElement Element, string Source, string Target)> edges,
        Dictionary<string, string> networkIdToName)
    {
        foreach (var (_, source, target) in edges)
        {
            if (source == nodeId && networkIdToName.ContainsKey(target))
                return networkIdToName[target];
            if (target == nodeId && networkIdToName.ContainsKey(source))
                return networkIdToName[source];
        }
        return null;
    }
}