using System.Xml.Linq;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Utils;

public class SimpleGraphMLGenerator : IUmlGraphGenerator
{
    private const string networkNodeFgColor = "#009999";
    private const string networkNodeBgColor = "#006363";
    private const string clientNodeFgColor = "#00CC00";
    private const string clientNodeBgColor = "#008500";
    private const string serverNodeColor = "#A64B00";

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
            nodeEl.SetAttributeValue("color", networkNodeBgColor);
            nodeEl.SetAttributeValue("background", networkNodeFgColor);
            nodeEl.SetAttributeValue("border", networkNodeBgColor);

            graphEl.Add(nodeEl);
        }
        foreach (var client in networkGraph.Clients)
        {
            var nodeEl = new XElement("node");
            nodeEl.SetAttributeValue("name", client.Value.Address);
            nodeEl.SetAttributeValue("id", client.Key);
            nodeEl.SetAttributeValue("color", clientNodeBgColor);
            nodeEl.SetAttributeValue("background", clientNodeFgColor);
            nodeEl.SetAttributeValue("border", clientNodeBgColor);
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

        foreach (var client in networkGraph.Clients)
        {
            visitedNetworks.Add(client.Key);

            var edgeEl = new XElement("edge");
            edgeEl.SetAttributeValue("id", $"e{client.Key}");
            edgeEl.SetAttributeValue("source", client.Key);
            edgeEl.SetAttributeValue("target", client.Value.Network!.Name);
            graphEl.Add(edgeEl);
        }
        return graphMl.ToString();
    }

    public string GenerateSequence(INetworkLogger monitoring, NetworkLoggerScope monitoringScope)
    {
        return string.Empty;
    }
}
