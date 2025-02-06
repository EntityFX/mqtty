// See https://aka.ms/new-console-template for more information
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

    public Network(string address, IMonitoring monitoring) : base(address, monitoring)
    {
    }

    public bool AddClient(IClient client)
    {
        if (client == null) throw new ArgumentNullException("client");

        if (_clients.ContainsKey(client.Address))
        {
            return false;
        }
        _clients[client.Address] = client;

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

        return _clients.Remove(clientNode.Address);
    }


    public bool Link(INetwork network)
    {
        if (network == null) throw new ArgumentNullException("network");

        if (_linkedNetworks.ContainsKey(network.Address))
        {
            return false;
        }
        _linkedNetworks[network.Address] = network;

        var result = network.Link(this);
        if (!result)
        {
            _linkedNetworks.Remove(network.Address);
        }

        monitoring.Push(this, network, null, MonitoringType.Link, new { });

        return true;
    }

    public bool Unlink(INetwork network)
    {
        if (network == null) throw new ArgumentNullException("network");

        if (!_linkedNetworks.ContainsKey(network.Address))
        {
            return false;
        }

        var result = network.Unlink(this);
        if (!result)
        {
            _linkedNetworks[network.Address] = network;
        }

        monitoring.Push(this, network, null, MonitoringType.Unlink, new { });

        return true;
    }

    public bool AddServer(IServer server)
    {
        if (server == null) throw new ArgumentNullException("server");

        if (_servers.ContainsKey(server.Address))
        {
            return false;
        }
        _servers[server.Address] = server;

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

    public override Task ReceiveAsync(Packet packet)
    {
        throw new NotImplementedException();
    }

    public override async Task SendAsync(Packet packet)
    {
        var sentToLocal = await SendToLocalAsync(packet);

        if (sentToLocal)
        {
            return;
        }

        var pathToRemote = GetPathToNetworkWeighted(packet.To, packet.DestinationType);

        var pathQueue = new Queue<Network>(pathToRemote);
        await SendToRemoteAsync(packet, pathQueue);
    }

    private async Task<bool> SendToLocalAsync(Packet packet)
    {
        if (string.IsNullOrEmpty(packet.To))
        {
            throw new ArgumentException($"'{nameof(packet.To)}' cannot be null or empty.", nameof(packet.To));
        }

        var destionationNode = GetDestinationNode(packet.To!, packet.DestinationType);

        if (destionationNode == null)
        {
            return false;
        }

        await destionationNode!.ReceiveAsync(packet);

        return true;
    }

    private async Task<bool> SendToRemoteAsync(Packet packet, Queue<Network> path)
    {
        if (!path.Any())
        {
            return false;
        }

        var next = path.Dequeue();

        var result = await next.SendToLocalAsync(packet);

        if (!result)
        {
            monitoring.Push(this, next, packet.packet, MonitoringType.Send, new { });
            await next.SendToRemoteAsync(packet, path);
        }

        return result;
    }

    public INode? FindNode(string address, NodeType type)
    {
        INode? result = GetDestinationNode(address, type);

        if (result != null)
        {
            return result;
        }

        var network = FindNodeNetwork(address, type);

        if (network == null)
        {
            return result;
        }

        result = network.GetDestinationNode(address, type);

        return result;
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
                if (id == Address)
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

    public IEnumerable<Network> GetPathToNetworkWeighted(string addres, NodeType nodeTypes)
    {
        var except = new List<string>();
        var allPaths = new List<List<Network>>();
        var length = 0;
        do
        {
            var path = new List<Network>();
            FindNodeNetworkWithExcept(null, this, addres, nodeTypes, path, except);

            if (path.Count == 0)
            {
                break;
            }

            allPaths.Add(path);
            length = path.Count;
        }
        while (length > 0);

        var shortest = allPaths.Select(p => (p.Count, p)).OrderBy(p => p.Count).FirstOrDefault();

        return shortest.p ?? Enumerable.Empty<Network>();
    }


    private Network? FindNodeNetwork(string address, NodeType nodeType)
    {
        var path = GetPathToNetworkWeighted(address, nodeType);

        return path?.Any() == true ? path.LastOrDefault() : null;
    }

    private Network? FindNodeNetwork(
        Network? previous, Network network, string address, NodeType nodeType, List<Network> path)
    {
        if (path.Count > 1000) { 
            return null;
        }

        var node = network.GetDestinationNode(address, nodeType);
        if (node != null)
        {
            return network;
        }
        foreach (var nn in network._linkedNetworks)
        {
            if (nn.Key.Equals(previous?.Address))
            {
                continue;
            }
            path.Add((Network)nn.Value);
            return FindNodeNetwork(network, (Network)nn.Value, address, nodeType, path);
        }

        return network;
    }

    private bool FindNodeNetworkWithExcept(
        Network? previous, Network network, string address, NodeType nodeType, List<Network> path, List<string> except)
    {
        var node = network.GetDestinationNode(address, nodeType);
        if (node != null)
        {
            except.Remove(network.Address);
            return true;
        }

        except.Add(network.Address);
        foreach (var nn in network._linkedNetworks)
        {
            if (except.Contains(nn.Key))
            {
                continue;
            }

            if (nn.Key.Equals(previous?.Address))
            {
                continue;
            }

            path.Add((Network)nn.Value);

            return FindNodeNetworkWithExcept(network, (Network)nn.Value, address, nodeType, path, except);
        }

        return false;
    }
}