using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Mqtt;
using System.Collections.Immutable;
using System.Net;
using EntityFX.MqttY.Contracts.Utils;

public class NetworkGraph : INetworkGraph
{
    private readonly INetworkBuilder _networkBuilder;
    private readonly Dictionary<(string Address, NodeType NodeType), ILeafNode> _nodes = new();
    private readonly Dictionary<string, INetwork> _networks = new();

    public NetworkGraph(
        INetworkBuilder networkBuilder, 
        IPathFinder pathFinder, 
        IMonitoring monitoring)
    {
        _networkBuilder = networkBuilder;
        PathFinder = pathFinder;
        Monitoring = monitoring;
        PathFinder.NetworkGraph = this;
    }

    public IPathFinder PathFinder { get; }

    public IMonitoring Monitoring { get; }

    public IReadOnlyDictionary<string, INetwork> Networks => _networks.ToImmutableDictionary();

    public IClient? BuildClient(string name, string protocolType, INetwork network)
    {
        if (_nodes.ContainsKey((name, NodeType.Client)))
        {
            return null;
        }

        var clientAddressFull = GetAddress(name, protocolType, network.Address);

        var client = _networkBuilder
            .ClientFactory.Create(
                new NodeBuildOptions(this, network, name, clientAddressFull, protocolType));

        if (client == null)
        {
            return null;
        }

        _nodes.Add((name, NodeType.Client), client);

        return client;
    }

    public TCLient? BuildClient<TCLient>(string name, string protocolType, INetwork network)
        where TCLient : IClient
    {
        return (TCLient?)BuildClient(name, protocolType, network);
    }

    public INetwork? BuildNetwork(string name, string address)
    {
        if (_networks.ContainsKey(address))
        {
            return null;
        }

        var network = new Network(name, address, this);
        _networks.Add(name, network);

        return network;
    }

    public ILeafNode? BuildNode(string name, string address, NodeType nodeType)
    {
        throw new NotImplementedException();
    }

    public IServer? BuildServer(string name, string protocolType, INetwork network)
    {
        if (_nodes.ContainsKey((name, NodeType.Server)))
        {
            return null;
        }
        var serverAddressFull = GetAddress(name, protocolType, network.Address);
        
        var server = _networkBuilder
            .ServerFactory.Create(
                new NodeBuildOptions(this, network, name, serverAddressFull, protocolType));

        if (server == null)
        {
            return null;
        }

        _nodes.Add((name, NodeType.Server), server);

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
            switch (node.Value.Type)
            {
                case NodeOptionType.Client when node.Value.ConnectsToServer == null:
                    continue;
                case NodeOptionType.Client:
                {
                    var nodeClient = GetNode(node.Key, NodeType.Client);

                    (nodeClient as IClient)?.Connect(node.Value.ConnectsToServer);
                    break;
                }
                case NodeOptionType.Server:
                {
                    var nodeServer = GetNode(node.Key, NodeType.Server);

                    (nodeServer as IServer)?.Start();
                    break;
                }
            }
        }
    }

    private void BuildNodes(NetworkGraphOptions options)
    {
        foreach (var node in options.Nodes)
        {
            var linkNetwork = _networks.GetValueOrDefault(node.Value.Network ?? string.Empty);
            if (linkNetwork == null) continue;

            switch (node.Value.Type)
            {
                case NodeOptionType.Server:
                {
                    var server = BuildServer(node.Key, node.Value.Specification ?? "tcp", linkNetwork);
                    break;
                }
                case NodeOptionType.Client:
                {
                    var client = BuildClient(node.Key, node.Value.Specification ?? "tcp", linkNetwork);
                    break;
                }
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
                if (link == null || link.Links == null || !_networks.ContainsKey(link.Links)) continue;

                _networks[networkOption.Key].Link(_networks[link.Links]);
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
        if (!_nodes.ContainsKey((address, nodeType)))
        {
            return null;
        }

        return _nodes[(address, nodeType)];
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
        if (!_nodes.ContainsKey((clientAddress, NodeType.Client)))
        {
            return;
        }

        var client = GetNode(clientAddress, NodeType.Client) as IClient;
        if (client == null)
        {
            return;
        }

        client.Disconnect();

        _nodes.Remove((clientAddress, NodeType.Client));

    }

    public void RemoveNetwork(string networkAddress)
    {
        if (_networks.ContainsKey(networkAddress))
        {
            return;
        }

        var network = _networks.GetValueOrDefault(networkAddress);
        if (network == null)
        {
            return;
        }

        network.UnlinkAll();

        _networks.Remove(networkAddress);
    }

    public void RemoveServer(string serverAddress)
    {
        if (!_nodes.ContainsKey((serverAddress, NodeType.Server)))
        {
            return;
        }

        var server = GetNode(serverAddress, NodeType.Client) as IServer;
        if (server == null)
        {
            return;
        }

        server.Stop();

        _nodes.Remove((serverAddress, NodeType.Client));
    }
}
