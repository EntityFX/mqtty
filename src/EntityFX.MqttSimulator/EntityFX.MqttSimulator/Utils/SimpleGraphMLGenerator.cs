using System.Xml.Linq;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Network;

namespace EntityFX.MqttY.Utils;

public class SimpleGraphMLGenerator : IUmlGraphGenerator
{
    private const string networkNodeFgColor = "#009999";
    private const string networkNodeBgColor = "#006363";
    private const string clientNodeFgColor = "#00CC00";
    private const string clientNodeBgColor = "#008500";
    private const string serverNodeFgColor = "#A64B00";
    private const string serverNodeBgColor = "#4BA600";

    private readonly XNamespace ns = XNamespace.Get("http://graphml.graphdrawing.org/xmlns");

    public string Generate(INetworkSimulator networkGraph)
    {
        var graphmlEl = new XElement(ns + "graphml");
  
        var graphMl = new XDocument(graphmlEl);
        var graphEl = new XElement(ns + "graph");
        graphEl.SetAttributeValue("id", "G");
        graphmlEl.Add(graphEl);

        foreach (var network in networkGraph.Networks)
        {
            AddNode( graphEl, network.Value, "n", networkNodeFgColor, networkNodeBgColor);
        }

        foreach (var client in networkGraph.Clients)
        {
            AddNode(graphEl, client.Value, "c", clientNodeFgColor, clientNodeBgColor);
        }


        foreach (var server in networkGraph.Servers)
        {
            AddNode(graphEl, server.Value, "s", serverNodeFgColor, serverNodeBgColor);
        }

        var visitedNetworks = new HashSet<string>();

        foreach (var network in networkGraph.Networks)
        {
            visitedNetworks.Add(network.Key);
            foreach (var nearestNetwork in network.Value.LinkedNearestNetworks)
            {
                if (visitedNetworks.Contains(nearestNetwork.Key)) continue;
                AddEdge(graphEl, network.Value, nearestNetwork.Value, "n");
            }
        }

        foreach (var client in networkGraph.Clients)
        {
            AddEdge(graphEl, client.Value, "c");
        }

        foreach (var server in networkGraph.Servers)
        {
            AddEdge(graphEl, server.Value, "s");
        }

        var wr = new StringWriter();
        graphMl.Save(wr);
        return wr.ToString();
    }

    private void AddEdge(XElement graphEl, ILeafNode source, string sourcePrefix)
    {
        AddEdge(graphEl, source, source.Network!, sourcePrefix);
    }

    private void AddEdge(XElement graphEl, INode source, INetwork target, string sourcePrefix)
    {
        var edgeEl = new XElement(ns + "edge");
        edgeEl.SetAttributeValue("id", $"e{source.Index}_{target.Index}");
        edgeEl.SetAttributeValue("source", $"{sourcePrefix}{source.Index}");
        edgeEl.SetAttributeValue("target", $"n{target!.Index}");
        graphEl.Add(edgeEl);
    }

    private void AddNode(XElement graphEl, INode node, string nodePrefix, string backColor, string borderColor)
    {
        var nodeEl = new XElement(ns + "node");
        nodeEl.SetAttributeValue("name", node.Address);
        nodeEl.SetAttributeValue("id", $"{nodePrefix}{node.Index}");
        nodeEl.SetAttributeValue("color", borderColor);
        nodeEl.SetAttributeValue("background", backColor);
        nodeEl.SetAttributeValue("border", borderColor);

        graphEl.Add(nodeEl);
    }

    public string GenerateSequence(INetworkLogger monitoring, NetworkLoggerScope monitoringScope)
    {
        return string.Empty;
    }
}
