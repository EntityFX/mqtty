using System.Diagnostics.Metrics;
using System.Drawing;
using System.Globalization;
using System.Xml.Linq;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Network;

namespace EntityFX.MqttY.Utils;

public class SimpleGraphMlGenerator : INetworkGraphFormatter
{
    private record NodeColor(byte R, byte G, byte B);
    
    private readonly XNamespace _ns = XNamespace.Get("http://graphml.graphdrawing.org/xmlns");

    public string SerializeNetworkGraph(INetworkSimulator networkGraph)
    {
        var graphmlEl = new XElement(_ns + "graphml");
        var graphMl = new XDocument(graphmlEl);
        var graphEl = new XElement(_ns + "graph");

        AddBaseAttributes(graphmlEl, networkGraph);
        HashSet<string> allUniqueCounters = GetUniqueCounterAttributes(networkGraph);
        AddCounterAttributes(graphmlEl, allUniqueCounters);

        graphEl.SetAttributeValue("id", "G");
        graphmlEl.Add(graphEl);


        foreach (var network in networkGraph.Networks)
        {
            var networkElement = AddNode(graphEl, network.Value, "n", 100.0m, new NodeColor(154, 150, 229));

            var allCounters = network.Value.Counters.GetAllGenericCounters();
            var statistics = network.Value.Counters.GetAllGenericCountersAsShortText();

            networkElement.Add(BuildDataElement("statistics", statistics));
            foreach (var counter in allCounters)
            {
                var elemName = $"s:{counter.Key}";
                networkElement.Add(BuildDataElement(elemName, counter.Value.Value));
            }
        }

        foreach (var client in networkGraph.Clients)
        {
            AddClientNode(graphEl, client.Value);
        }


        foreach (var server in networkGraph.Servers)
        {
            AddServerNode(graphEl, server.Value);
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

    private void AddCounterAttributes(XElement graphmlEl, HashSet<string> allUniqueCounters)
    {
        BuildKeyElement("statistics", "string", "node");
        foreach (var uniqueCounter in allUniqueCounters)
        {
            graphmlEl.Add(BuildKeyElement(uniqueCounter, "int", "node"));
        }
    }

    private static HashSet<string> GetUniqueCounterAttributes(INetworkSimulator networkGraph)
    {
        var allUniqueCounters = new HashSet<string>();
        foreach (var network in networkGraph.Networks)
        {
            var allCounters = network.Value.Counters.GetAllGenericCounters();

            foreach (var counter in allCounters)
            {
                var elemName = $"s:{counter.Key}";
                allUniqueCounters.Add(elemName);
            }
        }

        foreach (var client in networkGraph.Clients)
        {
            var allCounters = client.Value.Counters.GetAllGenericCounters();

            foreach (var counter in allCounters)
            {
                var elemName = $"s:{counter.Key}";
                allUniqueCounters.Add(elemName);
            }
        }

        foreach (var server in networkGraph.Servers)
        {
            var allCounters = server.Value.Counters.GetAllGenericCounters();

            foreach (var counter in allCounters)
            {
                var elemName = $"s:{counter.Key}";
                allUniqueCounters.Add(elemName);
            }
        }

        return allUniqueCounters;
    }

    private XElement AddServerNode(XElement graphEl, IServer server)
    {
        var nodeEl = AddNode(graphEl, server, "s", 70.0m, new NodeColor(249, 119, 67));

        var allCounters = server.Counters.GetAllGenericCounters();
        var statistics = server.Counters.GetAllGenericCountersAsShortText();
        nodeEl.Add(BuildDataElement("statistics", statistics));
        foreach (var counter in allCounters)
        {
            var elemName = $"s:{counter.Key}";
            nodeEl.Add(BuildDataElement(elemName, counter.Value.Value));
        }

        return nodeEl;
    }


    private XElement AddClientNode(XElement graphEl, IClient client)
    {
        var nodeEl = AddNode(graphEl, client, "c", 50.0m, new NodeColor(40, 179, 106));

        if (!client.IsConnected)
        {
            return nodeEl;
        }

        nodeEl.Add(BuildDataElement("connectsTo", client.ServerName ?? string.Empty));
        nodeEl.Add(BuildDataElement("connectsId", $"s{client.ServerIndex}"));

        var allCounters = client.Counters.GetAllGenericCounters();
        var statistics = client.Counters.GetAllGenericCountersAsShortText();
        nodeEl.Add(BuildDataElement("statistics", statistics));

        foreach (var counter in allCounters)
        {
            var elemName = $"s:{counter.Key}";
            nodeEl.Add(BuildDataElement(elemName, counter.Value.Value));
        }

        return nodeEl;
    }

    private void AddEdge(XElement graphEl, ILeafNode source, string sourcePrefix)
    {
        AddEdge(graphEl, source, source.Network!, sourcePrefix);
    }

    private void AddEdge(XElement graphEl, INode source, INetwork target, string sourcePrefix)
    {
        var edgeEl = new XElement(_ns + "edge");
        edgeEl.SetAttributeValue("id", $"e{source.Index}_{target.Index}");
        edgeEl.SetAttributeValue("source", $"{sourcePrefix}{source.Index}");
        edgeEl.SetAttributeValue("target", $"n{target!.Index}");
        
        edgeEl.Add(BuildDataElement("weight", 1.0));
        
        graphEl.Add(edgeEl);
    }

    private XElement AddNode(XElement graphEl, INode node, string nodePrefix, decimal size, NodeColor? nodeColor)
    {
        var nodeEl = new XElement(_ns + "node");
        nodeEl.SetAttributeValue("id", $"{nodePrefix}{node.Index}");
        nodeEl.Add(BuildDataElement("label", node.Name));
        nodeEl.Add(BuildDataElement("address", node.Address));
        nodeEl.Add(BuildDataElement("type", node.NodeType.ToString()));
        nodeEl.Add(BuildDataElement("size", size.ToString(CultureInfo.InvariantCulture)));

        if (nodeColor != null)
        {
            nodeEl.Add(BuildDataElement("r", nodeColor.R.ToString(CultureInfo.InvariantCulture)));
            nodeEl.Add(BuildDataElement("g", nodeColor.G.ToString(CultureInfo.InvariantCulture)));
            nodeEl.Add(BuildDataElement("b", nodeColor.B.ToString(CultureInfo.InvariantCulture)));
        }

        graphEl.Add(nodeEl);

        return nodeEl;
    }

    private void AddBaseAttributes(XElement graphml, INetworkSimulator networkGraph)
    {
        graphml.Add(BuildKeyElement("label", "string", "node"));
        graphml.Add(BuildKeyElement("weight", "double", "edge"));
        graphml.Add(BuildKeyElement("r", "int", "node"));
        graphml.Add(BuildKeyElement("g", "int", "node"));
        graphml.Add(BuildKeyElement("b", "int", "node"));
        graphml.Add(BuildKeyElement("size", "float", "node"));
        graphml.Add(BuildKeyElement("type", "string", "node"));
        graphml.Add(BuildKeyElement("address", "string", "node"));
        graphml.Add(BuildKeyElement("statistics", "string", "node"));

        AddAttributes(graphml, networkGraph);
    }

    protected virtual void AddAttributes(XElement graphml, INetworkSimulator networkGraph)
    {
        graphml.Add(BuildKeyElement("connectsTo", "string", "node"));
        graphml.Add(BuildKeyElement("connectsId", "string", "node"));
    }

    private XElement BuildKeyElement(string name, string type, string forElement)
    {
        var keyElement = new XElement(_ns + "key");
        keyElement.SetAttributeValue("attr.name", name);
        keyElement.SetAttributeValue("attr.type", type);
        keyElement.SetAttributeValue("for", forElement);
        keyElement.SetAttributeValue("id", name);
        return keyElement;
    }
    
    private XElement BuildDataElement(string key, object value)
    {
        var keyElement = new XElement(_ns + "data");
        keyElement.SetAttributeValue("key", key);
        keyElement.SetValue(value);
        return keyElement;
    }
}
