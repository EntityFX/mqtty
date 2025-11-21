using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Counter;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using NetworkLoggerType = EntityFX.MqttY.Contracts.NetworkLogger.NetworkLoggerType;

namespace EntityFX.MqttY.Network;

public class Network : NodeBase, INetwork
{
    private readonly Dictionary<string, INetwork> _linkedNetworks = new();
    private readonly Dictionary<string, IServer> _servers = new();
    private readonly Dictionary<string, IClient> _clients = new();
    private readonly Dictionary<string, IApplication> _applications = new();

    /// <summary>
    /// TODO: Add max size limit
    /// </summary>
    private readonly ConcurrentDictionary<Guid, NetworkMonitoringPacket> _monitoringPacketsQueue = new();
    private readonly NetworkTypeOption _networkTypeOption;

    //private Dictionary<Guid, NetworkMonitoringPacket> _monitoringPacketsQueue = new();

    private readonly TicksOptions _ticksOptions;

    private readonly NetworkCounters _networkCounters;
    private CounterGroup _counters;

    private readonly object _countersLock = new object();
    private CancellationTokenSource? _cancelTokenSource;
    public string NetworkType { get; }

    public override CounterGroup Counters
    {
        get
        {
            _counters.Counters = new ICounter[]
                {
                    _networkCounters,
                    new CounterGroup("Servers")
                    {
                        Counters = _servers.Values.ToArray().Select(s =>s.Counters).ToArray()
                    },
                    new CounterGroup("Clients")
                    {
                        Counters = _clients.Values.ToArray().Select(s =>s.Counters).ToArray()
                    },
                    new CounterGroup("Applications")
                    {
                        Counters = _applications.Values.ToArray().Select(s =>s.Counters).ToArray()
                    },
                };
            return _counters;
        }
        set
        {
            _counters = value;
        }
    }

    public IReadOnlyDictionary<string, INetwork> LinkedNearestNetworks => _linkedNetworks.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IServer> Servers => _servers.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IClient> Clients => _clients.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IApplication> Applications => _applications.ToImmutableDictionary();

    public IReadOnlyDictionary<string, INode> Nodes => _clients.Values.OfType<INode>().ToArray()
        .Concat(_servers.Values.OfType<INode>().ToArray())
        .Concat(_applications.Values.OfType<INode>().ToArray())
        .ToDictionary(n => n.Name, n => n).ToImmutableDictionary();


    public override NodeType NodeType => NodeType.Network;

    public long QueueSize => _monitoringPacketsQueue.Count;

    public Network(
        int index, string name, string address, string networkType,
        NetworkTypeOption networkTypeOption, TicksOptions ticksOptions)
        : base(index, name, address)
    {
        this._networkTypeOption = networkTypeOption;
        this._ticksOptions = ticksOptions;
        _networkCounters = new NetworkCounters("Network", ticksOptions);
        _counters = new CounterGroup(Name);
        NetworkType = networkType;
    }

    public bool AddClient(IClient client)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));

        if (_clients.ContainsKey(client.Address))
        {
            return false;
        }
        _clients[client.Name] = client;

        ((Client)client).Network = this;

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
        if (network == null) throw new ArgumentNullException(nameof(network));

        if (_linkedNetworks.ContainsKey(network.Name))
        {
            return false;
        }
        _linkedNetworks[network.Name] = network;

        var result = network.Link(this);

        NetworkSimulator!.Monitoring.Push(NetworkSimulator.TotalTicks, this, network, null, NetworkLoggerType.Link,
            $"Link network {this.Name} to {network.Name}", "Network", "Link", Scope);

        return true;
    }

    public bool Unlink(INetwork network)
    {
        if (network == null) throw new ArgumentNullException(nameof(network));

        if (!_linkedNetworks.ContainsKey(network.Name))
        {
            return false;
        }

        var result = network.Unlink(this);
        if (!result)
        {
            _linkedNetworks[network.Name] = network;
        }

        NetworkSimulator!.Monitoring.Push(NetworkSimulator.TotalTicks, this, network, null, NetworkLoggerType.Unlink,
            $"Unlink network {this.Name} from {network.Name}", "Network", "Unlink");

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
        if (server == null) throw new ArgumentNullException(nameof(server));

        if (_servers.ContainsKey(server.Name))
        {
            return false;
        }
        _servers[server.Name] = server;

        ((Server)server).Network = this;

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

    internal void TransferNext(NetworkMonitoringPacket networkMonitoringPacket)
    {
        if (networkMonitoringPacket.Path.Count == 0)
        {
            networkMonitoringPacket.Type = NetworkPacketType.Local;
        }

        //_monitoringPacketsQueue.Add(networkMonitoringPacket.Packet.Id, networkMonitoringPacket);
        _monitoringPacketsQueue.AddOrUpdate(networkMonitoringPacket.Packet.Id, networkMonitoringPacket, (g, p) => p);

        _networkCounters.CountInbound(networkMonitoringPacket.Packet);
    }

    //TODO: If queue limit is exceeded then reject Send
    //bool?
    //timeout?
    protected override bool SendImplementation(NetworkPacket packet)
    {
        var networkPacket = GetNetworkPacketType(packet);

        if (_networkCounters.AvgInboundThroughput > _networkTypeOption.Speed * 10)
        {
            _networkCounters.Refuse();
            return false;
        }

        if (_monitoringPacketsQueue.Count > 50000)
        {
            _networkCounters.Refuse();
            return false;
        }

        // _monitoringPacketsQueue.Add(packet.Id, networkPacket);
        _monitoringPacketsQueue.AddOrUpdate(networkPacket.Packet.Id, networkPacket, (g, p) => p);

        _networkCounters.CountInbound(packet);

        return true;
    }

    private NetworkMonitoringPacket GetNetworkPacketType(NetworkPacket packet)
    {
        var destionationNode = GetDestinationNode(packet.To!, packet.ToType);

        if (destionationNode != null)
        {
            return new NetworkMonitoringPacket(packet, new Queue<INetwork>(), NetworkPacketType.Local, destionationNode);
        }

        var sourceNode = NetworkSimulator!.GetNode(packet.From, packet.FromType);

        var fromNetwork = NetworkSimulator.GetNetworkByNode(packet.From, packet.FromType);

        var toNetwork = NetworkSimulator.GetNetworkByNode(packet.To, packet.ToType);

        if (sourceNode == null || fromNetwork == null || toNetwork == null)
        {
            return new NetworkMonitoringPacket(packet, new Queue<INetwork>(), NetworkPacketType.Unreachable, null);
        }

        var pathToRemote = NetworkSimulator.PathFinder.GetPathToNetwork(fromNetwork.Name, toNetwork.Name);

        var pathQueue = new Queue<INetwork>(pathToRemote);
        destionationNode = (toNetwork as Network)?.GetDestinationNode(packet.To!, packet.ToType);

        //var waitTime = _monitoringPacketsQueue.Count <= 5000 ? ticksOptions.NetworkTicks :
        //        _monitoringPacketsQueue.Count / 5000 * ticksOptions.NetworkTicks;

        var waitTime = _networkTypeOption.RefreshTicks;

        return new NetworkMonitoringPacket(packet, pathQueue, NetworkPacketType.Remote, destionationNode)
        {
            WaitTime = waitTime
        };
    }


    private bool SendToLocal(INetwork network, NetworkMonitoringPacket networkPacket)
    {
        var packet = networkPacket.Packet;
        if (string.IsNullOrEmpty(packet.From))
        {
            throw new ArgumentException($"'{nameof(packet.To)}' cannot be null or empty.", nameof(packet.To));
        }

        if (networkPacket.DestionationNode == null)
        {
            return false;
        }


        NetworkSimulator!.Monitoring.Push(NetworkSimulator.TotalTicks, network, networkPacket.DestionationNode, packet.Payload, NetworkLoggerType.Receive,
            $"Push packet from network {network.Name} to node {networkPacket.DestionationNode.Name}",
            "Network", packet.Category, packet.Scope, packet.Ttl, queueLength: _monitoringPacketsQueue.Count);
        NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);

        _networkCounters.CountOutbound(packet);
        return networkPacket.DestionationNode!.Receive(packet);
    }


    private bool SendToRemote(NetworkMonitoringPacket networkPacket)
    {
        var packet = networkPacket.Packet;

        if (!networkPacket.Path.Any())
        {
            return false;
        }

        var next = networkPacket.Path.Dequeue() as Network;

        if (next == null)
        {
            return false;
        }

        packet.DecrementTtl();

        if (packet.Ttl == 0)
        {
            NetworkSimulator!.Monitoring.Push(NetworkSimulator.TotalTicks, this, next, packet.Payload, NetworkLoggerType.Unreachable,
                $"NetworkMonitoringPacket unreachable: {packet.From} to {packet.To}", "Network", packet.Category, packet.Scope);
            //destination uneachable
            return false;
        }

        NetworkSimulator!.Monitoring.Push(NetworkSimulator.TotalTicks, this, next, packet.Payload, NetworkLoggerType.Push,
            $"Push packet from network {this.Name} to {next.Name}",
            "Network", packet.Category, packet.Scope, packet.Ttl, queueLength: _monitoringPacketsQueue.Count);


        _networkCounters.CountTransfers();
        _networkCounters.CountOutbound(networkPacket.Packet);
        next.TransferNext(networkPacket);

        return true;
    }

    protected override bool ReceiveImplementation(NetworkPacket packet)
    {
        return true;
    }

    //TODO: add tick reaction
    public override void Refresh()
    {

        foreach (var pendingPacket in _monitoringPacketsQueue)
        {
            pendingPacket.Value.ReduceWaitTime();
            if (pendingPacket.Value.WaitTime > 0)
            {
                continue;
            }
            var result = ProcessTransferPacket(pendingPacket.Value);
            //if (!result)
            //{
            //    pendingPacket.Value.WaitTime = 5;
            //    continue;
            //}
            _monitoringPacketsQueue.Remove(pendingPacket.Key, out var t);
        }

        //foreach (var pendingPacket in _monitoringPacketsQueue)
        //{
        //    if (pendingPacket.Value.WaitTime > 0)
        //    {
        //        continue;
        //    }

        //}



        _networkCounters.SetQueueLength(_monitoringPacketsQueue.Count);
        Counters.Refresh(NetworkSimulator!.TotalTicks);
    }


    //TODO: need VIRTUAL wait 
    private bool ProcessTransferPacket(NetworkMonitoringPacket networkPacket)
    {
        var result = false;
        var packet = networkPacket.Packet;
        var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref packet!,
            $"Transfer packet {packet.From} to {packet.To}");

        if (networkPacket.Type == NetworkPacketType.Local)
        {
            result = SendToLocal(this, networkPacket);
        }
        else if (networkPacket.Type == NetworkPacketType.Remote)
        {
            result = SendToRemote(networkPacket);
        }
        return result;
    }

    public INode? FindNode(string address, NodeType type)
    {
        return NetworkSimulator!.GetNode(address, type);
    }

    public override string ToString()
    {
        return $"N: {Address}";
    }

    internal ISender? GetDestinationNode(string id, NodeType destinationNodeType)
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

    protected override void BeforeReceive(Contracts.Network.NetworkPacket packet)
    {
    }

    protected override void AfterReceive(Contracts.Network.NetworkPacket packet)
    {
    }

    protected override void BeforeSend(Contracts.Network.NetworkPacket packet)
    {
    }

    protected override void AfterSend(Contracts.Network.NetworkPacket packet)
    {
    }

    public bool AddApplication(IApplication application)
    {
        if (application == null) throw new ArgumentNullException(nameof(application));

        if (_applications.ContainsKey(application.Name))
        {
            return false;
        }
        _applications[application.Name] = application;

        ((ApplicationBase)application).Network = this;

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

    public override void Reset()
    {
    }
}