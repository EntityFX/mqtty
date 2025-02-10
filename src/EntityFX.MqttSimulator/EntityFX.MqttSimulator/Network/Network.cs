using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using MonitoringType = EntityFX.MqttY.Contracts.Monitoring.MonitoringType;

public class Network : NodeBase, INetwork
{
    private readonly Dictionary<string, INetwork> _linkedNetworks = new();
    private readonly Dictionary<string, IServer> _servers = new();
    private readonly Dictionary<string, IClient> _clients = new();

    public IReadOnlyDictionary<string, INetwork> LinkedNearestNetworks => _linkedNetworks.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IServer> Servers => _servers.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IClient> Clients => _clients.ToImmutableDictionary();


    public override NodeType NodeType => NodeType.Network;

    public Network(string name, string address, INetworkGraph networkGraph) : base(name, address, networkGraph)
    {
    }

    public bool AddClient(IClient client)
    {
        if (client == null) throw new ArgumentNullException("client");

        if (_clients.ContainsKey(client.Address))
        {
            return false;
        }
        _clients[client.Name] = client;

        return true;
    }

    public bool RemoveClient(string client)
    {
        var clientNode = _clients.GetValueOrDefault(client);
        if (clientNode == null)
        {
            return false;
        }

        if (clientNode.IsConnected)
        {
            clientNode.Disconnect();
        }

        return _clients.Remove(clientNode.Name);
    }


    public bool Link(INetwork network)
    {
        if (network == null) throw new ArgumentNullException("network");

        if (_linkedNetworks.ContainsKey(network.Name))
        {
            return false;
        }
        _linkedNetworks[network.Name] = network;

        var result = network.Link(this);

        networkGraph.Monitoring.Push(this, network, null, MonitoringType.Link, new { });

        return true;
    }

    public bool Unlink(INetwork network)
    {
        if (network == null) throw new ArgumentNullException("network");

        if (!_linkedNetworks.ContainsKey(network.Name))
        {
            return false;
        }

        var result = network.Unlink(this);
        if (!result)
        {
            _linkedNetworks[network.Name] = network;
        }

        networkGraph.Monitoring.Push(this, network, null, MonitoringType.Unlink, new { });

        return true;
    }

    public bool UnlinkAll()
    {
        foreach (var network in _linkedNetworks.Values) 
        { 
            var result = network.UnlinkAll();
            if (!result)
            {
                return false;
            }
        }

        return true;
    }

    public bool AddServer(IServer server)
    {
        if (server == null) throw new ArgumentNullException("server");

        if (_servers.ContainsKey(server.Name))
        {
            return false;
        }
        _servers[server.Name] = server;

        return true;
    }

    public bool RemoveServer(string id)
    {
        if (!_servers.ContainsKey(id))
        {
            return false;
        }

        _servers.Remove(id);

        return true;
    }

    public override Task<Packet> ReceiveAsync(Packet packet)
    {
        return SendAsync(packet);
    }

    public override Task<Packet> SendAsync(Packet packet)
    {
        return Task.Run(async () =>
        {
            networkGraph.Monitoring.Push(
            packet.From, packet.ToType, Address, NodeType.Network,
            packet.Payload, MonitoringType.Push, new { });

            var sentToLocal = await SendToLocalAsync(this, packet);

            if (sentToLocal != null)
            {
                return sentToLocal!;
            }

            var fromNetwork = networkGraph.GetNetworkByNode(packet.From, packet.FromType);

            var toNetwork = networkGraph.GetNetworkByNode(packet.To, packet.ToType);

            if (fromNetwork == null || toNetwork == null)
            {
                return networkGraph.GetReversePacket(packet, packet.Payload);
            }

            var pathToRemote = networkGraph.PathFinder.GetPathToNetwork(fromNetwork.Name, toNetwork.Name);

            var pathQueue = new Queue<INetwork>(pathToRemote);

            return await SendToRemoteAsync(packet, pathQueue) ?? networkGraph.GetReversePacket(packet, packet.Payload);
        });

    }

    private async Task<Packet?> SendToLocalAsync(INetwork network, Packet packet)
    {
        if (string.IsNullOrEmpty(packet.From))
        {
            throw new ArgumentException($"'{nameof(packet.To)}' cannot be null or empty.", nameof(packet.To));
        }

        var destionationNode = GetDestinationNode(packet.To!, packet.ToType);

        if (destionationNode == null)
        {
            return null;
        }
        networkGraph.Monitoring.Push(
            network.Address, NodeType.Network, packet.To, packet.ToType,
            packet.Payload, MonitoringType.Push, new { });

        return await destionationNode!.ReceiveAsync(packet);
    }

    private async Task<Packet?> SendToRemoteAsync(Packet packet, Queue<INetwork> path)
    {
        if (!path.Any())
        {
            return null;
        }

        var next = path.Dequeue() as Network;

        if (next == null)
        {
            return null;
        }

        networkGraph.Monitoring.Push(this, next, packet.Payload, MonitoringType.Push, new { });
        var result = await next.SendToLocalAsync(next, packet);

        if (result == null)
        {
            return await next.SendToRemoteAsync(packet, path);
        }

        return result;
    }

    public INode? FindNode(string address, NodeType type)
    {
        return networkGraph.GetNode(address, type);
    }

    public override string ToString()
    {
        return $"N: {Address}";
    }

    private INode? GetDestinationNode(string id, NodeType destinationNodeType)
    {
        INode? result = null;
        switch (destinationNodeType)
        {
            case NodeType.Network:
                if (id == Name)
                {
                    result = this;
                }
                break;
            case NodeType.Server:
                result = _servers.GetValueOrDefault(id);
                break;
            case NodeType.Client:
                result = _clients.GetValueOrDefault(id);
                break;
            default:
                break;
        }

        return result;
    }
}