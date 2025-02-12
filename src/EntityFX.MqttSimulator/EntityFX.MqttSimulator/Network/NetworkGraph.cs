using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Mqtt;
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

    public IClient? BuildClient(string name, string protocolType, INetwork network)
    {
        if (nodes.ContainsKey((name, NodeType.Client)))
        {
            return null;
        }

        var clientAddressFull = GetAddress(name, protocolType, network.Address);

        IClient? client = null;

        if (protocolType == "mqtt")
        {
            client = new MqttClient(name, clientAddressFull, protocolType, network, this, name);
        } else
        {
            client = new Client(name, clientAddressFull, protocolType, network, this);
        }

        nodes.Add((name, NodeType.Client), client);

        return client;
    }

    public TCLient? BuildClient<TCLient>(string name, string protocolType, INetwork network)
        where TCLient : IClient
    {
        return (TCLient?)BuildClient(name, protocolType, network);
    }

    public INetwork? BuildNetwork(string name, string address)
    {
        if (networks.ContainsKey(address))
        {
            return null;
        }

        var network = new Network(name, address, this);
        networks.Add(name, network);

        return network;
    }

    public ILeafNode? BuildNode(string name, string address, NodeType nodeType)
    {
        throw new NotImplementedException();
    }

    public IServer? BuildServer(string name, string protocolType, INetwork network)
    {
        if (nodes.ContainsKey((name, NodeType.Server)))
        {
            return null;
        }
        var serverAddressFull = GetAddress(name, protocolType, network.Address);

        IServer? server = null;
        if (protocolType == "mqtt")
        {
            server = new MqttBroker(name, serverAddressFull, protocolType, network, this);
        }
        else
        {
            server = new Server(name, serverAddressFull, protocolType, network, this);
        }

        nodes.Add((name, NodeType.Server), server);

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
            BuildNetwork(networkOption.Key, networkOption.Key);
        }

        ConfigureLinks(options);

        PathFinder.Build();

        BuildNodes(options);

        ConfigureNodes(options);
    }

    private void ConfigureNodes(NetworkGraphOptions options)
    {
        foreach (var node in options.Nodes)
        {
            if (node.Value.Type == NodeOptionType.Client)
            {
                if (node.Value.ConnectsToServer == null) continue;

                var nodeClient = GetNode(node.Key, NodeType.Client);

                (nodeClient as IClient)?.Connect(node.Value.ConnectsToServer);
            }
        }
    }

    private void BuildNodes(NetworkGraphOptions options)
    {
        foreach (var node in options.Nodes)
        {
            var linkNetwork = networks.GetValueOrDefault(node.Value.Network ?? string.Empty);
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
    }

    private void ConfigureLinks(NetworkGraphOptions options)
    {
        foreach (var networkOption in options.Networks)
        {
            if (networkOption.Value?.Any() != true) continue;

            foreach (var link in networkOption.Value)
            {
                if (link == null || link.Links == null || !networks.ContainsKey(link.Links)) continue;

                networks[networkOption.Key].Link(networks[link.Links]);
            }
        }
    }

    public string GetAddress(string name, string protocolType, string networkAddress)
    {
        return $"{protocolType}://{name}.{networkAddress}";
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

    public Packet GetReversePacket(Packet packet, byte[] payload)
    {
        return new Packet(
            To: packet.From,
            From: packet.To,
            Payload: payload,
            FromType: packet.ToType,
            ToType: packet.FromType
        );
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
