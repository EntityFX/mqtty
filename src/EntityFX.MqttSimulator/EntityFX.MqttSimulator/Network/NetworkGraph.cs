using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using System.Collections.Immutable;

public class NetworkGraph : INetworkGraph
{
    private readonly Dictionary<(string Address, NodeType NodeType), ILeafNode> nodes = new();
    private readonly Dictionary<string, INetwork> networks = new();

    public NetworkGraph(IPathFinder pathFinder, IMonitoring monitoring)
    {
        this.PathFinder = pathFinder;
        this.Monitoring = monitoring;
        PathFinder.NetworkGraph = this;
    }

    public IPathFinder PathFinder { get; }

    public IMonitoring Monitoring { get; }

    public IReadOnlyDictionary<string, INetwork> Networks => networks.ToImmutableDictionary();

    public IClient? BuildClient(string address, INetwork network)
    {
        if (nodes.ContainsKey((address, NodeType.Client)))
        {
            return null;
        }

        var client = new Client(address, network, this);
        nodes.Add((address, NodeType.Client), client);

        return client;
    }

    public INetwork? BuildNetwork(string address)
    {
        if (networks.ContainsKey(address))
        {
            return null;
        }

        var network = new Network(address, this);
        networks.Add(address, network);

        return network;
    }

    public ILeafNode? BuildNode(string address, NodeType nodeType)
    {
        throw new NotImplementedException();
    }

    public IServer? BuildServer(string address, INetwork network)
    {
        if (nodes.ContainsKey((address, NodeType.Server)))
        {
            return null;
        }

        var server = new Server(address, network, this);
        nodes.Add((address, NodeType.Server), server);

        return server;
    }

    public INetwork? GetNodeNetwork(string address, NodeType nodeType)
    {
        return GetNode(address, nodeType)?.Network;
    }

    public ILeafNode? GetNode(string address, NodeType nodeType)
    {
        if (!nodes.ContainsKey((address, nodeType)))
        {
            return null;
        }

        return nodes[(address, nodeType)];
    }

    public void RemoveClient(INetwork network)
    {
        throw new NotImplementedException();
    }

    public void RemoveNetwork(INetwork network)
    {
        throw new NotImplementedException();
    }

    public void RemoveServer(INetwork network)
    {
        throw new NotImplementedException();
    }
}
