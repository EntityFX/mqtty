using System.Text;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Network;

namespace EntityFX.MqttY.Utils;

public class PlantUmlGraphGenerator
{
    public string GenerateSequence(INetworkLogger monitoring, NetworkLoggerScope monitoringScope)
    {
        var plantUmlBuilder = new StringBuilder();
        plantUmlBuilder.AppendLine("@startuml");

        var visitedNodes = new HashSet<string>();
        var sequenceItems = new LinkedList<string>();
        VisitItemsForSequence(monitoringScope, visitedNodes, sequenceItems, 0);

        foreach (var item in sequenceItems)
        {
            plantUmlBuilder.AppendLine(item);
        }

        GenerateGroupSequence(monitoringScope, plantUmlBuilder);

        plantUmlBuilder.AppendLine("@enduml");

        return plantUmlBuilder.ToString();
    }

    private static void VisitItemsForSequence(NetworkLoggerScope monitoringScope, HashSet<string> visitedNodes, LinkedList<string> items, int level)
    {
        NetworkLoggerItem? destination = null;

        foreach (var item in monitoringScope.Items)
        {
            if (item is NetworkLoggerItem monitoringItem)
            {
                var nodeType = GetNodeType(monitoringItem.SourceType);

                if (visitedNodes.Contains(monitoringItem.From)) continue;

                if (monitoringItem.From == monitoringScope.Source)
                {
                    visitedNodes.Add(monitoringItem.From);
                    items.AddFirst($"{nodeType} {monitoringItem.From}");
                }

                if (monitoringItem.To == monitoringScope.Destination)
                {
                    destination = monitoringItem;
                }

                if (visitedNodes.Contains(monitoringItem.From)) continue;
                visitedNodes.Add(monitoringItem.From);
                items.AddLast($"{nodeType} {monitoringItem.From}");


                if (visitedNodes.Contains(monitoringItem.To)) continue;
                //visitedNodes.Add(monitoringItem.To);
                //nodeType = GetNodeType(monitoringItem.DestinationType);
                //items.AddLast($"{nodeType} {monitoringItem.To}");
            }
            else if (item is NetworkLoggerScope itemScope)
            {
                VisitItemsForSequence(itemScope, visitedNodes, items, level++);
            }
        }



        if (destination != null && level == 0)
        {
            if (visitedNodes.Contains(destination.To)) return;

            var nodeType = GetNodeType(destination.DestinationType);
            visitedNodes.Add(destination.To);
            items.AddLast($"{nodeType} {destination.To}");
        }
    }

    private static void GenerateGroupSequence(NetworkLoggerScope monitoringScope, StringBuilder plantUmlBuilder)
    {
        plantUmlBuilder.AppendLine($"group {monitoringScope.ScopeLabel}");

        foreach (var item in monitoringScope.Items)
        {
            if (item is NetworkLoggerItem monitoringItem)
            {
                if (string.IsNullOrEmpty(monitoringItem.From) || string.IsNullOrEmpty(monitoringItem.To))
                    continue;

                var arrow = monitoringItem.Type == NetworkLoggerType.Push ? "->" : "-->";

                plantUmlBuilder.AppendLine($"{monitoringItem.From} {arrow} {monitoringItem.To} " +
                    $": {{{monitoringItem.Type}}} {monitoringItem.Category} [Tick={monitoringItem.Tick}]");
            }
            else if (item is NetworkLoggerScope innerScope)
            {
                GenerateGroupSequence(innerScope, plantUmlBuilder);
            }
        }
        plantUmlBuilder.AppendLine($"end");
    }

    private static string GetNodeType(NodeType nodeType)
    {
        return nodeType switch
        {
            NodeType.Client => "actor",
            NodeType.Server => "control",
            NodeType.Network => "participant",
            _ => "participant"
        };
    }

    public string Generate(INetworkSimulator networkGraph)
    {
        var plantUmlBuilder = new StringBuilder();
        plantUmlBuilder.AppendLine("@startuml");
        //plantUmlBuilder.AppendLine("left to right direction");

        var sortedNetworks = networkGraph.Networks.Values.OrderBy(v => v.Index).ToArray();
        AppendBlockNode(plantUmlBuilder, "rectangle", string.Empty, "network", null, null);
        foreach (var network in sortedNetworks)
        {
            AppendNode(plantUmlBuilder, "cloud", network.Name, null, "A9DCDF");
        }
        plantUmlBuilder.AppendLine("}");
        plantUmlBuilder.AppendLine();

        var visitedNetworks = new HashSet<string>();

        foreach (var network in sortedNetworks)
        {
            visitedNetworks.Add(network.Name);
            foreach (var nearestNetwork in network.LinkedNearestNetworks)
            {
                if (visitedNetworks.Contains(nearestNetwork.Key)) continue;
                plantUmlBuilder.AppendLine($"{network.Name} <--> {nearestNetwork.Key}");
            }
        }

        var visitedGroups = new HashSet<string>();
        AppendNodes(plantUmlBuilder, sortedNetworks, visitedGroups);
        visitedGroups.Clear();
        AppendNetworkConnections(plantUmlBuilder, sortedNetworks, visitedGroups);

        plantUmlBuilder.AppendLine("@enduml");

        return plantUmlBuilder.ToString();
    }

    private static void AppendNodes(StringBuilder plantUmlBuilder, INetwork[] sortedNetworks, HashSet<string> visitedGroups)
    {
        foreach (var network in sortedNetworks)
        {
            if (network.Servers?.Any() != true && network.Applications?.Any() != true) continue;

            AppendBlockNode(plantUmlBuilder, "rectangle", $"Nodes of {network.Name} network", "nodes", null, null);
            AppendClients(plantUmlBuilder, visitedGroups, network.Clients, true);

            if (network.Servers?.Any() == true)
            {
                foreach (var server in network.Servers)
                {
                    if (server.Value.Group != null && visitedGroups.Contains(server.Value.Group))
                    {
                        continue;
                    }
                    AppendNode(plantUmlBuilder, "rectangle", server.Key,
                        server.Value.ProtocolType, "E3664A");
                    if (server.Value.Group != null)
                    {
                        visitedGroups.Add(server.Value.Group);
                    }
                }
            }

            if (network.Applications?.Any() == true)
            {
                foreach (var application in network.Applications)
                {
                    if (application.Value.Group != null && visitedGroups.Contains(application.Value.Group))
                    {
                        continue;
                    }
                    AppendApplicationNode(plantUmlBuilder, application.Key,
                        application.Value, "4AE366");
                    if (application.Value.Group != null)
                    {
                        visitedGroups.Add(application.Value.Group);
                    }
                }
            }
            plantUmlBuilder.AppendLine("}");
            plantUmlBuilder.AppendLine();
        }
    }

    private static void AppendClients(StringBuilder plantUmlBuilder, HashSet<string> visitedGroups, IReadOnlyDictionary<string, IClient> items, bool checkParent)
    {
        foreach (var client in items)
        {
            if ((checkParent && client.Value.Parent != null) || client.Value.Group != null && visitedGroups.Contains(client.Value.Group))
            {
                continue;
            }
            AppendNode(plantUmlBuilder, "circle", client.Value.Group ?? client.Key,
                client.Value.ProtocolType, "ADD1B2", client.Value.GroupAmount > 0 ? $"Count clients: {client.Value.GroupAmount}" : null);
            if (client.Value.Group != null)
            {
                visitedGroups.Add(client.Value.Group);
            }
        }
    }

    private static void AppendNetworkConnections(StringBuilder plantUmlBuilder, INetwork[] sortedNetworks, HashSet<string> visitedGroups)
    {
        foreach (var network in sortedNetworks)
        {
            foreach (var client in network.Clients)
            {
                if (client.Value.Group != null && visitedGroups.Contains(client.Value.Group))
                {
                    continue;
                }
                AppendConnection(plantUmlBuilder, client.Value, client.Key, network);
                if (client.Value.Group != null)
                {
                    visitedGroups.Add(client.Value.Group);
                }
            }

            foreach (var server in network.Servers)
            {
                if (server.Value.Group != null && visitedGroups.Contains(server.Value.Group))
                {
                    continue;
                }
                AppendConnection(plantUmlBuilder, server.Value, server.Key, network);
                if (server.Value.Group != null)
                {
                    visitedGroups.Add(server.Value.Group);
                }
            }

            foreach (var application in network.Applications)
            {
                if (application.Value.Group != null && visitedGroups.Contains(application.Value.Group))
                {
                    continue;
                }
                AppendConnection(plantUmlBuilder, application.Value, application.Key, network);
                if (application.Value.Group != null)
                {
                    visitedGroups.Add(application.Value.Group);
                }
            }
        }
    }

    private static StringBuilder AppendConnection(StringBuilder plantUmlBuilder, ILeafNode from, string key, INode to)
    {
        var arrow = from.Parent == null ? "-->" : "..>";
        return plantUmlBuilder.AppendLine($"{from.Group ?? key} {arrow} {to.Name}");
    }

    private static void AppendNode(StringBuilder plantUmlBuilder,
        string nodeType, string name, string? stereotype = null, string? color = null, string? comment = null)
    {
        plantUmlBuilder.AppendLine($"{nodeType} {name} " +
                                   $"{(stereotype != null ? $"<<{stereotype}>>" : "")}" +
                                   $"{(color != null ? $" #{color}" : "")}" +
                                   $"{(!string.IsNullOrEmpty(comment) ? " [" : "")}");

        if (comment != null)
        {
            plantUmlBuilder.AppendLine(name);
            plantUmlBuilder.AppendLine("---");
            plantUmlBuilder.AppendLine(comment);
            plantUmlBuilder.AppendLine("]");
        }
    }

    private static void AppendApplicationNode(StringBuilder plantUmlBuilder,
    string name, IApplication application, string? color = null, string? comment = null)
    {
        var stereotype = application.ProtocolType;

        var hasItems = application.Clients.Any() == true;
        plantUmlBuilder.AppendLine($"component \"{name}\" " +
                                   $"{(stereotype != null ? $"<<{stereotype}>>" : "")}" +
                                   $"{(color != null ? $" #{color}" : "")}" +
                                   $"{(hasItems ? " {" : "")}");

        if (hasItems)
        {
            var visitedGroups = new HashSet<string>();

            AppendClients(plantUmlBuilder, visitedGroups, application.Clients, false);

            plantUmlBuilder.AppendLine("}");
            plantUmlBuilder.AppendLine();
        }
    }

    private static void AppendBlockNode(StringBuilder plantUmlBuilder,
        string nodeType, string name, string stereotype, string? color = null, string? comment = null)
    {
        plantUmlBuilder.AppendLine($"{nodeType} {(string.IsNullOrEmpty(name) ? string.Empty : $"\"{name}\"")} " +
                                   $"{(stereotype != null ? $"<<{stereotype}>>" : "")}" +
                                   $"{(color != null ? $" #{color}" : "")}" +
                                   $" {{");
    }
}