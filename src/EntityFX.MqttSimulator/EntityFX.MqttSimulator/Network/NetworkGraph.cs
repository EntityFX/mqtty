using System;
using System.Collections.Immutable;
using System.Net.Sockets;
using System.Reflection;
using System.Xml.Linq;
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

    public IClient? BuildClient(int index, string name, string protocolType,
        INetwork network, string? group = null, int? groupAmount = null, Dictionary<string, string[]>? additional = default)
    {
        if (_nodes.ContainsKey((name, NodeType.Client)))
        {
            return null;
        }

        var clientAddressFull = GetAddress(name, protocolType, network.Address);

        var client = _networkBuilder
            .ClientFactory.Create(
                new NodeBuildOptions<Dictionary<string, string[]>>(this, network, index, name, clientAddressFull, group, groupAmount, protocolType, null, additional));

        if (client == null)
        {
            return null;
        }

        _nodes.Add((name, NodeType.Client), client);

        return client;
    }

    public TCLient? BuildClient<TCLient>(int index, string name, string protocolType, INetwork network,
        string? group = null, int? groupAmount = null, Dictionary<string, string[]>? additional = default)
        where TCLient : IClient
    {
        return (TCLient?)BuildClient(index, name, protocolType, network, group, groupAmount, additional);
    }

    public INetwork? BuildNetwork(int index, string name, string address)
    {
        if (_networks.ContainsKey(address))
        {
            return null;
        }
        var network = _networkBuilder
            .NetworkFactory.Create(
                new NodeBuildOptions<Dictionary<string, string[]>>(this, null, index, name, address, null, null, String.Empty, null, new()));

        if (network == null)
        {
            return null;
        }

        _networks.Add(name, network);

        return network;
    }

    public ILeafNode? BuildNode(int index, string name, string address, NodeType nodeType, string? group = null, int? groupAmount = null,
        Dictionary<string, string[]>? additional = null)
    {
        return null;
    }

    public IServer? BuildServer(int index, string name, string protocolType, INetwork network,
        string? group = null, int? groupAmount = null, Dictionary<string, string[]>? additional = null)
    {
        if (_nodes.ContainsKey((name, NodeType.Server)))
        {
            return null;
        }
        var serverAddressFull = GetAddress(name, protocolType, network.Address);

        var server = _networkBuilder
            .ServerFactory.Create(
                new NodeBuildOptions<Dictionary<string, string[]>>(this, network, index, name, serverAddressFull, group, groupAmount, protocolType, null, additional));

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
        var servers = options.Nodes.Where(nt => nt.Value.Type == NodeOptionType.Server).ToArray();
        foreach (var node in servers)
        {
            var nodeServer = GetNode(node.Key, NodeType.Server);

            (nodeServer as IServer)?.Start();
        }

        var clients = options.Nodes.Where(nt => nt.Value.Type == NodeOptionType.Client).ToArray();
        foreach (var node in clients)
        {
            if (node.Value.Quantity > 1)
            {
                Enumerable.Range(1, node.Value.Quantity.Value).ToList()
                    .ForEach(
                        (nc) =>
                        {
                            var nodeClient = GetNode($"{node.Key}{nc}", NodeType.Client) as IClient;

                            if (nodeClient == null)
                            {
                                return;
                            }

                            var bo = new NodeBuildOptions<Dictionary<string, string[]>>(
                                this, nodeClient.Network, nodeClient.Index, nodeClient.Name, nodeClient.Address,
                                nodeClient.Group, nodeClient.GroupAmount, nodeClient.ProtocolType, node.Value.ConnectsToServer, node.Value.Additional);

                            _networkBuilder.ClientFactory.Configure(bo, nodeClient);
                        });
            }
            else
            {
                var nodeClient = GetNode(node.Key, NodeType.Client) as IClient;
                if (nodeClient == null) continue;

                var bo = new NodeBuildOptions<Dictionary<string, string[]>>(
                    this, nodeClient.Network, nodeClient.Index, nodeClient.Name, nodeClient.Address,
                    nodeClient.Group, nodeClient.GroupAmount, nodeClient.ProtocolType, node.Value.ConnectsToServer, node.Value.Additional);
                _networkBuilder.ClientFactory.Configure(bo, nodeClient);
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
                        BuildServer(index, node.Key, node.Value.Specification ?? "tcp", linkNetwork, null, null, node.Value.Additional);
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
                                        node.Value.Specification ?? "tcp", linkNetwork, node.Key, node.Value.Quantity, node.Value.Additional));
                        }
                        else
                        {
                            BuildClient(index, node.Key, node.Value.Specification ?? "tcp", linkNetwork, null, null, node.Value.Additional);
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

    public Packet GetReversePacket(Packet packet, byte[] payload, string? category)
    {
        return new Packet(
            To: packet.From,
            From: packet.To,
            Payload: payload,
            FromType: packet.ToType,
            ToType: packet.FromType,
            Category: category ?? packet.Category,
            Scope: packet.Scope
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

    public void Refresh()
    {
        var scope = Monitoring.BeginScope("Refresh network graph");
        Monitoring.Push(MonitoringType.Refresh, $"Refresh whole network", scope);

        var bytes = Array.Empty<byte>();

        foreach (var network in _networks)
        {
            Monitoring.Push(
                network.Value, network.Value, bytes, MonitoringType.Refresh, $"Refresh network {network.Key}", scope);
            network.Value.Refresh();
        }

        foreach (var node in _nodes)
        {
            Monitoring.Push(
                node.Value, node.Value, bytes, MonitoringType.Refresh, $"Refresh node {node.Key}", scope);
            node.Value.Refresh();
        }

        Monitoring.EndScope(scope);
    }

    public void Tick(INode nodeBase)
    {
        Monitoring.Tick();
    }

    public IApplication? BuildApplication<TConfiguration>(
        int index, string name, string protocolType, INetwork network, string? group = null, int? groupAmount = null, TConfiguration? applicationConfig = default)
    {
        if (_nodes.ContainsKey((name, NodeType.Application)))
        {
            return null;
        }
        var serverAddressFull = GetAddress(name, protocolType, network.Address);

        var server = _networkBuilder
            .ApplicationFactory.Create(
                new NodeBuildOptions<object>(this, network, index, name, serverAddressFull, group, groupAmount, protocolType, null, applicationConfig));

        if (server == null)
        {
            return null;
        }

        _nodes.Add((name, NodeType.Server), server);

        return server;
    }
}