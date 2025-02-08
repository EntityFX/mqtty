using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using System.Collections.Immutable;
using System.Net;

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

    public IClient? BuildClient(string clientAddress, string protocolType, INetwork network)
    {
        if (nodes.ContainsKey((clientAddress, NodeType.Client)))
        {
            return null;
        }

        var clientAddressFull = GetFullName(clientAddress, protocolType, network.Address);
        var client = new Client(clientAddressFull, protocolType, network, this);
        nodes.Add((clientAddressFull, NodeType.Client), client);

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

    public IServer? BuildServer(string serverAddress, string protocolType, INetwork network)
    {
        if (nodes.ContainsKey((serverAddress, NodeType.Server)))
        {
            return null;
        }
        var serverAddressFull = $"{protocolType}://{serverAddress}.{network.Address}";
        var server = new Server(serverAddressFull, protocolType, network, this);
        nodes.Add((serverAddressFull, NodeType.Server), server);

        return server;
    }

    public void Configure(NetworkGraphOptions options)
    {
        if (options.Networks.Any() != true)
        {
            return;
        }

        foreach (var networkOption in options.Networks)
        {
            BuildNetwork(networkOption.Key);
        }

        foreach (var networkOption in options.Networks)
        {
            if (networkOption.Value?.Any() != true) continue;

            foreach (var link in networkOption.Value)
            {
                if (link.Links == null || !networks.ContainsKey(link.Links)) continue;

                networks[networkOption.Key].Link(networks[link.Links]);
            }
        }

        foreach (var node in options.Nodes)
        {
            var linkNetwork = networks.GetValueOrDefault(node.Value.Links ?? string.Empty);
            if (linkNetwork == null) continue;

            if (node.Value.Type == NodeOptionType.Server)
            {
                var server = BuildServer(node.Key, node.Value.Specification ?? "tcp", linkNetwork);
                if (server == null) continue;

                server.Start();
            }

            if (node.Value.Type == NodeOptionType.Client)
            {
                var client = BuildClient(node.Key, node.Value.Specification ?? "tcp", linkNetwork);
                if (client == null) continue;
            }
        }

        foreach (var node in options.Nodes)
        {

            if (node.Value.Type == NodeOptionType.Client)
            {
                if (node.Value.Connects == null) continue;

                var nodeClient = GetNode(GetFullName(node.Key, node.Value.Specification ?? "tcp", node.Value.Links), NodeType.Client);

                (nodeClient as IClient)?.Connect(node.Value.Connects);

               //node.Connect(nodeServer);
            }
        }
    }

    public string GetFullName(string clientAddress, string protocolType, string networkAddress)
    {
        return $"{protocolType}://{clientAddress}.{networkAddress}";
    }

    public INetwork? GetNetworkByNode(string address, NodeType nodeType)
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

    public void RemoveClient(string clientAddress)
    {
        if (!nodes.ContainsKey((clientAddress, NodeType.Client)))
        {
            return;
        }

        var client = GetNode(clientAddress, NodeType.Client) as IClient;
        if (client == null)
        {
            return;
        }

        client.Disconnect();

        nodes.Remove((clientAddress, NodeType.Client));

    }

    public void RemoveNetwork(string networkAddress)
    {
        if (networks.ContainsKey(networkAddress))
        {
            return;
        }

        var network = networks.GetValueOrDefault(networkAddress);
        if (network == null)
        {
            return;
        }

        network.UnlinkAll();

        networks.Remove(networkAddress);
    }

    public void RemoveServer(string serverAddress)
    {
        throw new NotImplementedException();
    }
}
