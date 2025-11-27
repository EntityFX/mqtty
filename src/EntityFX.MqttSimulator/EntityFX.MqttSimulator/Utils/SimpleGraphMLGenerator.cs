using System.Xml.Linq;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Utils;

public class SimpleGraphMLGenerator : IUmlGraphGenerator
{
    public string Generate(INetworkSimulator networkGraph)
    {
        var ns = XNamespace.Get("http://graphml.graphdrawing.org/xmlns");
        var graphmlEl = new XElement(ns + "graphml");
  
        var graphMl = new XDocument(graphmlEl);
        var graphEl = new XElement("graph");
        graphEl.SetAttributeValue("id", "G");
        graphmlEl.Add(graphEl);

        foreach (var network in networkGraph.Networks)
        {
            var nodeEl = new XElement("node");
            nodeEl.SetAttributeValue("name", network.Value.Address);
            nodeEl.SetAttributeValue("id", network.Key);

            graphEl.Add(nodeEl);
        }

        var visitedNetworks = new HashSet<string>();

        foreach (var network in networkGraph.Networks)
        {
            visitedNetworks.Add(network.Key);
            foreach (var nearestNetwork in network.Value.LinkedNearestNetworks)
            {
                if (visitedNetworks.Contains(nearestNetwork.Key)) continue;
                var edgeEl = new XElement("edge");
                edgeEl.SetAttributeValue("id", $"e{network.Key}");
                edgeEl.SetAttributeValue("source", network.Key);
                edgeEl.SetAttributeValue("target", nearestNetwork.Key);
                graphEl.Add(edgeEl);
            }
        }
        return graphMl.ToString();
    }

    public string GenerateSequence(INetworkLogger monitoring, NetworkLoggerScope monitoringScope)
    {
        return string.Empty;
    }
}
