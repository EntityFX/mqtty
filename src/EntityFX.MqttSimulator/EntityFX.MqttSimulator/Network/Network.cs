using System.Collections.Immutable;
using EntityFX.MqttY.Contracts.Network;
using MonitoringType = EntityFX.MqttY.Contracts.Monitoring.MonitoringType;

namespace EntityFX.MqttY.Network;

public class Network : NodeBase, INetwork
{
    private readonly Dictionary<string, INetwork> _linkedNetworks = new();
    private readonly Dictionary<string, IServer> _servers = new();
    private readonly Dictionary<string, IClient> _clients = new();
    private readonly Dictionary<string, IApplication> _applications = new();

    public IReadOnlyDictionary<string, INetwork> LinkedNearestNetworks => _linkedNetworks.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IServer> Servers => _servers.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IClient> Clients => _clients.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IApplication> Applications => _applications.ToImmutableDictionary();


    public override NodeType NodeType => NodeType.Network;

    public Network(int index, string name, string address, INetworkGraph networkGraph) 
        : base(index, name, address, networkGraph)
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

        NetworkGraph.Monitoring.Push(this, network, null, MonitoringType.Link, $"Link network {this.Name} to {network.Name}", "Network", "Link");

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

        NetworkGraph.Monitoring.Push(this, network, null, MonitoringType.Unlink, $"Unlink network {this.Name} from {network.Name}", "Network", "Unlink");

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

    public override Task<Packet> ReceiveWithResponseAsync(Packet packet)
    {
        return SendWithResponseAsync(packet);
    }

    public override Task ReceiveAsync(Packet packet)
    {
        return SendAsync(packet);
    }

    public override Task<Packet> SendWithResponseAsync(Packet packet)
    {
        return Task.Run(async () =>
        {
            var scope = NetworkGraph.Monitoring.WithBeginScope(ref packet!, $"Push packet from {packet.From} to {packet.To}");

            var result = await SendToLocalWithResponseAsync(this, packet);

            if (result != null)
            {
                NetworkGraph.Monitoring.WithEndScope(ref packet);
                return result!;
            }

            var sourceNode = NetworkGraph.GetNode(packet.From, packet.FromType);

            var fromNetwork = NetworkGraph.GetNetworkByNode(packet.From, packet.FromType);

            var toNetwork = NetworkGraph.GetNetworkByNode(packet.To, packet.ToType);

            if (sourceNode == null || fromNetwork == null || toNetwork == null)
            {
                result = NetworkGraph.GetReversePacket(packet, packet.Payload, packet.Category);
                NetworkGraph.Monitoring.WithEndScope(ref result);
                return result;
            }

            NetworkGraph.Monitoring.Push(sourceNode, fromNetwork, packet.Payload, MonitoringType.Push, 
                $"Push packet {sourceNode.Name} to network {fromNetwork.Name}", "Network", packet.Category, packet.Scope, packet.Ttl);

            var pathToRemote = NetworkGraph.PathFinder.GetPathToNetwork(fromNetwork.Name, toNetwork.Name);

            var pathQueue = new Queue<INetwork>(pathToRemote);

            result =  await SendToRemoteWithResponseAsync(packet, pathQueue) ?? NetworkGraph.GetReversePacket(packet, packet.Payload, packet.Category);
            NetworkGraph.Monitoring.WithEndScope(ref result);
            return result;
        });

    }

    public override Task SendAsync(Packet packet)
    {
        return Task.Run(async () =>
        {
            var scope = NetworkGraph.Monitoring.WithBeginScope(ref packet!, $"Transfer packet {packet.From} to {packet.To}");
            //NetworkGraph.Monitoring.Push(packet, MonitoringType.Push, packet.Category, scope);

            var sentToLocal = await SendToLocalAsync(this, packet);

            if (sentToLocal == true)
            {
                return;
            }

            var sourceNode = NetworkGraph.GetNode(packet.From, packet.FromType);

            var fromNetwork = NetworkGraph.GetNetworkByNode(packet.From, packet.FromType);

            var toNetwork = NetworkGraph.GetNetworkByNode(packet.To, packet.ToType);

            if (sourceNode == null || fromNetwork == null || toNetwork == null)
            {
                return;
            }

            //NetworkGraph.Monitoring.Push(sourceNode, fromNetwork, packet.Payload, MonitoringType.Push, packet.Category, packet.Scope, packet.Ttl);

            var pathToRemote = NetworkGraph.PathFinder.GetPathToNetwork(fromNetwork.Name, toNetwork.Name);

            var pathQueue = new Queue<INetwork>(pathToRemote);

            var result = await SendToRemoteAsync(packet, pathQueue);
            if (result)
            {
                return;
            }
        });
    }


    private async Task<Packet?> SendToLocalWithResponseAsync(INetwork network, Packet packet)
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

        Tick();

        NetworkGraph.Monitoring.Push(network, destionationNode, packet.Payload, MonitoringType.Push, 
            $"Push packet from network {network.Name} to node {destionationNode.Name}", "Network", packet.Category, packet.Scope, packet.Ttl);
        NetworkGraph.Monitoring.WithEndScope(ref packet);
        return await destionationNode!.ReceiveWithResponseAsync(packet);
    }

    private async Task<bool> SendToLocalAsync(INetwork network, Packet packet)
    {
        if (string.IsNullOrEmpty(packet.From))
        {
            throw new ArgumentException($"'{nameof(packet.To)}' cannot be null or empty.", nameof(packet.To));
        }

        var destionationNode = GetDestinationNode(packet.To!, packet.ToType);

        if (destionationNode == null)
        {
            return false;
        }

        Tick();

        NetworkGraph.Monitoring.Push(network, destionationNode, packet.Payload, MonitoringType.Receive, 
            $"Push packet from network {network.Name} to node {destionationNode.Name}", "Network", packet.Category, packet.Scope, packet.Ttl);
        NetworkGraph.Monitoring.WithEndScope(ref packet);
        await destionationNode!.ReceiveAsync(packet);

        return true;
    }

    private async Task<Packet?> SendToRemoteWithResponseAsync(Packet packet, Queue<INetwork> path)
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

        Tick();
        packet.DecrementTtl();

        if (packet.Ttl == 0)
        {
            NetworkGraph.Monitoring.Push(this, next, packet.Payload, MonitoringType.Unreachable, 
                $"Packet unreachable: {packet.From} to {packet.To}", "Network", packet.Category, packet.Scope, packet.Ttl);
            //destination uneachable
            return packet;
        }
        NetworkGraph.Monitoring.Push(this, next, packet.Payload, MonitoringType.Push, 
            $"Push packet from network {this.Name} to {next.Name}", "Network", packet.Category, packet.Scope, packet.Ttl);
        var result = await next.SendToLocalWithResponseAsync(next, packet);

        if (result == null)
        {
             await next.SendToRemoteWithResponseAsync(packet, path);
        }

        return result;
    }

    private async Task<bool> SendToRemoteAsync(Packet packet, Queue<INetwork> path)
    {
        if (!path.Any())
        {
            return false;
        }

        var next = path.Dequeue() as Network;

        if (next == null)
        {
            return false;
        }

        Tick();
        packet.DecrementTtl();

        if (packet.Ttl == 0)
        {
            NetworkGraph.Monitoring.Push(this, next, packet.Payload, MonitoringType.Unreachable, 
                $"Packet unreachable: {packet.From} to {packet.To}", "Network", packet.Category, packet.Scope);
            //destination uneachable
            return false;
        }

        NetworkGraph.Monitoring.Push(this, next, packet.Payload, MonitoringType.Push, 
            $"Push packet from network {this.Name} to {next.Name}", "Network", packet.Category, packet.Scope, packet.Ttl);
        var result = await next.SendToLocalAsync(next, packet);

        if (!result)
        {
            result = await next.SendToRemoteAsync(packet, path);
        }

        return result;
    }

    public INode? FindNode(string address, NodeType type)
    {
        return NetworkGraph.GetNode(address, type);
    }

    public override string ToString()
    {
        return $"N: {Address}";
    }

    private ISender? GetDestinationNode(string id, NodeType destinationNodeType)
    {
        ISender? result = null;
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

    protected override void BeforeReceive(Packet packet)
    {
    }

    protected override void AfterReceive(Packet packet)
    {
    }

    protected override void BeforeSend(Packet packet)
    {
    }

    protected override void AfterSend(Packet packet)
    {
    }

    public bool AddApplication(IApplication application)
    {
        if (application == null) throw new ArgumentNullException("application");

        if (_applications.ContainsKey(application.Name))
        {
            return false;
        }
        _applications[application.Name] = application;

        return true;
    }

    public bool RemoveApplication(string application)
    {
        if (!_applications.ContainsKey(application))
        {
            return false;
        }

        _applications.Remove(application);

        return true;
    }
}