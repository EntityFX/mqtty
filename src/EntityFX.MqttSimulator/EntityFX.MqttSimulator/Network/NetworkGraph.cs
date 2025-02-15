using System.Collections.Immutable;
using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Network;

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

    public IClient? BuildClient(int index, string name, string protocolType, INetwork network, string? group = null)
    {
        if (_nodes.ContainsKey((name, NodeType.Client)))
        {
            return null;
        }

        var clientAddressFull = GetAddress(name, protocolType, network.Address);

        var client = _networkBuilder
            .ClientFactory.Create(
                new NodeBuildOptions(this, network, index, name, clientAddressFull, group, protocolType));

        if (client == null)
        {
            return null;
        }

        _nodes.Add((name, NodeType.Client), client);

        return client;
    }

    public TCLient? BuildClient<TCLient>(int index, string name, string protocolType, INetwork network, string? group = null)
        where TCLient : IClient
    {
        return (TCLient?)BuildClient(index, name, protocolType, network, group);
    }

    public INetwork? BuildNetwork(int index, string name, string address)
    {
        if (_networks.ContainsKey(address))
        {
            return null;
        }
        var network = _networkBuilder
            .NetworkFactory.Create(
                new NodeBuildOptions(this, null, index, name, address, null, String.Empty));

        if (network == null)
        {
            return null;
        }
        
        _networks.Add(name, network);

        return network;
    }

    public ILeafNode? BuildNode(int index, string name, string address, NodeType nodeType, string? group = null)
    {
        return null;
    }

    public IServer? BuildServer(int index, string name, string protocolType, INetwork network, string? group = null)
    {
        if (_nodes.ContainsKey((name, NodeType.Server)))
        {
            return null;
        }
        var serverAddressFull = GetAddress(name, protocolType, network.Address);
        
        var server = _networkBuilder
            .ServerFactory.Create(
                new NodeBuildOptions(this, network, index, name, serverAddressFull, group, protocolType));

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
            BuildNetwork(networkOption.Value.Index, networkOption.Key, networkOption.Key);
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

                    if (node.Value.Quantity > 1)
                    {
                        Enumerable.Range(1, node.Value.Quantity.Value).ToList()
                            .ForEach(
                                (nc) =>
                                {
                                    var nodeClient = GetNode($"{node.Key}{nc}", NodeType.Client);
                                    (nodeClient as IClient)?.Connect(node.Value.ConnectsToServer);
                                });
                    }
                    else
                    {
                        var nodeClient = GetNode(node.Key, NodeType.Client);
                        (nodeClient as IClient)?.Connect(node.Value.ConnectsToServer);
                    }
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
        var index = 0;
        foreach (var node in options.Nodes)
        {
            var linkNetwork = _networks.GetValueOrDefault(node.Value.Network ?? string.Empty);
            if (linkNetwork == null) continue;

            switch (node.Value.Type)
            {
                case NodeOptionType.Server:
                {
                    BuildServer(index, node.Key, node.Value.Specification ?? "tcp", linkNetwork);
                    break;
                }
                case NodeOptionType.Client:
                {
                    if (node.Value.Quantity > 1)
                    {
                        Enumerable.Range(1, node.Value.Quantity.Value).ToList()
                            .ForEach(
                                (nc) => BuildClient(
                                    index, $"{node.Key}{nc}", 
                                    node.Value.Specification ?? "tcp", linkNetwork, node.Key));
                    }
                    else
                    {
                        BuildClient(index, node.Key, node.Value.Specification ?? "tcp", linkNetwork);
                    }
                    

                    break;
                }
            }

            index++;
        }
    }

    private void ConfigureLinks(NetworkGraphOptions options)
    {
        foreach (var networkOption in options.Networks)
        {
            if (networkOption.Value?.Links?.Any() != true) continue;

            foreach (var link in networkOption.Value.Links)
            {
                if (link?.Network == null || !_networks.ContainsKey(link.Network)) continue;

                _networks[networkOption.Key].Link(_networks[link.Network]);
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