using System.Text;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Utils;

public class PlantUmlGraphGenerator
{
    public string Generate(INetworkGraph networkGraph)
    {
        var plantUmlBuilder = new StringBuilder();
        plantUmlBuilder.AppendLine("@startuml");
        plantUmlBuilder.AppendLine("left to right direction");

        var sortedNetworks = networkGraph.Networks.Values.OrderBy(v => v.Index).ToArray();
        foreach (var network in sortedNetworks)
        {
            plantUmlBuilder.AppendLine($"cloud {network.Name}");
        }

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
        foreach (var network in sortedNetworks)
        {
            foreach (var client in network.Clients)
            {
                if (client.Value.Group != null && visitedGroups.Contains(client.Value.Group))
                {
                    continue;
                }
                plantUmlBuilder.AppendLine($"circle {client.Key}");
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
                plantUmlBuilder.AppendLine($"rectangle {server.Key}");
                if (server.Value.Group != null)
                {
                    visitedGroups.Add(server.Value.Group);
                }
            }
        }
        visitedGroups.Clear();
        foreach (var network in sortedNetworks)
        {
            foreach (var client in network.Clients)
            {
                if (client.Value.Group != null && visitedGroups.Contains(client.Value.Group))
                {
                    continue;
                }
                plantUmlBuilder.AppendLine($"{client.Key} --> {network.Name}");
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
                plantUmlBuilder.AppendLine($"{server.Key} --> {network.Name}");
                if (server.Value.Group != null)
                {
                    visitedGroups.Add(server.Value.Group);
                }
            }
        }
        
        plantUmlBuilder.AppendLine("@enduml");

        return plantUmlBuilder.ToString();
    }
}