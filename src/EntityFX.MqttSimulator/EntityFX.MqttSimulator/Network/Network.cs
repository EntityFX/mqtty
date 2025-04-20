using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Counter;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Xml.Linq;
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
    private readonly ConcurrentBag<NetworkMonitoringPacket> _monitoringPacketsQueue = new();
    //private Dictionary<Guid, NetworkMonitoringPacket> _monitoringPacketsQueue = new();

    private readonly TicksOptions ticksOptions;

    private readonly NetworkCounters counters;

    private readonly object _countersLock = new object();

    public override CounterGroup Counters
    {
        get
        {

            return new CounterGroup(Name)
            {
                Counters = new ICounter[]
                {
                    counters,
                    new CounterGroup("Servers")
                    {
                        Counters = _servers.Values.ToArray().Select(s =>s.Counters)
                    },
                    new CounterGroup("Clients")
                    {
                        Counters = _clients.Values.ToArray().Select(s =>s.Counters).ToArray()
                    },
                    new CounterGroup("Applications")
                    {
                        Counters = _applications.Values.ToArray().Select(s =>s.Counters).ToArray()
                    },
                }
            };

        }
    }

    public IReadOnlyDictionary<string, INetwork> LinkedNearestNetworks => _linkedNetworks.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IServer> Servers => _servers.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IClient> Clients => _clients.ToImmutableDictionary();

    public IReadOnlyDictionary<string, IApplication> Applications => _applications.ToImmutableDictionary();


    public override NodeType NodeType => NodeType.Network;

    public long QueueSize => _monitoringPacketsQueue.Count;

    public Network(int index, string name, string address, INetworkSimulator networkGraph, TicksOptions ticksOptions)
        : base(index, name, address, networkGraph)
    {
        this.ticksOptions = ticksOptions;
        counters = new NetworkCounters("Network", ticksOptions);
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

        NetworkGraph.Monitoring.Push(NetworkGraph.TotalTicks, this, network, null, NetworkLoggerType.Link,
            $"Link network {this.Name} to {network.Name}", "Network", "Link", Scope);

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

        NetworkGraph.Monitoring.Push(NetworkGraph.TotalTicks, this, network, null, NetworkLoggerType.Unlink,
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

    internal void TransferNext(NetworkMonitoringPacket networkMonitoringPacket)
    {
        if (networkMonitoringPacket.Path.Count == 0)
        {
            networkMonitoringPacket.Type = NetworkPacketType.Local;
        }

        //_monitoringPacketsQueue.Add(networkMonitoringPacket.Packet.Id, networkMonitoringPacket);
        _monitoringPacketsQueue.Add(networkMonitoringPacket);

        counters.CountInbound(networkMonitoringPacket.Packet);
    }

    //TODO: If queue limit is exceeded then reject Send
    //bool?
    //timeout?
    protected override Task<bool> SendImplementationAsync(NetworkPacket packet)
    {
        var networkPacket = GetNetworkPacketType(packet);

        if (_monitoringPacketsQueue.Count > 5000)
        {
            counters.Refuse();
            return Task.FromResult(false);
        }

        // _monitoringPacketsQueue.Add(packet.Id, networkPacket);
        _monitoringPacketsQueue.Add(networkPacket);

        counters.CountInbound(packet);

        return Task.FromResult(true);
    }

    private NetworkMonitoringPacket GetNetworkPacketType(NetworkPacket packet)
    {
        var destionationNode = GetDestinationNode(packet.To!, packet.ToType);

        if (destionationNode != null)
        {
            return new NetworkMonitoringPacket(packet, new Queue<INetwork>(), NetworkPacketType.Local, destionationNode);
        }

        var sourceNode = NetworkGraph.GetNode(packet.From, packet.FromType);

        var fromNetwork = NetworkGraph.GetNetworkByNode(packet.From, packet.FromType);

        var toNetwork = NetworkGraph.GetNetworkByNode(packet.To, packet.ToType);

        if (sourceNode == null || fromNetwork == null || toNetwork == null)
        {
            return new NetworkMonitoringPacket(packet, new Queue<INetwork>(), NetworkPacketType.Unreachable, null);
        }

        var pathToRemote = NetworkGraph.PathFinder.GetPathToNetwork(fromNetwork.Name, toNetwork.Name);

        var pathQueue = new Queue<INetwork>(pathToRemote);
        destionationNode = (toNetwork as Network)?.GetDestinationNode(packet.To!, packet.ToType);

        var waitTime = _monitoringPacketsQueue.Count <= 5000 ? ticksOptions.NetworkTicks :
                _monitoringPacketsQueue.Count / 5000 * ticksOptions.NetworkTicks;

        return new NetworkMonitoringPacket(packet, pathQueue, NetworkPacketType.Remote, destionationNode)
        {
            WaitTime = waitTime
        };
    }


    private async Task<bool> SendToLocalAsync(INetwork network, NetworkMonitoringPacket networkPacket)
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


        NetworkGraph.Monitoring.Push(NetworkGraph.TotalTicks, network, networkPacket.DestionationNode, packet.Payload, NetworkLoggerType.Receive,
            $"Push packet from network {network.Name} to node {networkPacket.DestionationNode.Name}",
            "Network", packet.Category, packet.Scope, packet.Ttl, queueLength: _monitoringPacketsQueue.Count);
        NetworkGraph.Monitoring.WithEndScope(NetworkGraph.TotalTicks, ref packet);

        counters.CountOutbound(packet);
        await networkPacket.DestionationNode!.ReceiveAsync(packet);

        return true;
    }


    private Task<bool> SendToRemoteAsync(NetworkMonitoringPacket networkPacket)
    {
        var packet = networkPacket.Packet;
        if (packet == null)
        {
            return Task.FromResult(false);
        }


        if (!networkPacket.Path.Any())
        {
            return Task.FromResult(false);
        }

        var next = networkPacket.Path.Dequeue() as Network;

        if (next == null)
        {
            return Task.FromResult(false);
        }

        packet.DecrementTtl();

        if (packet.Ttl == 0)
        {
            NetworkGraph.Monitoring.Push(NetworkGraph.TotalTicks, this, next, packet.Payload, NetworkLoggerType.Unreachable,
                $"NetworkMonitoringPacket unreachable: {packet.From} to {packet.To}", "Network", packet.Category, packet.Scope);
            //destination uneachable
            return Task.FromResult(false);
        }

        NetworkGraph.Monitoring.Push(NetworkGraph.TotalTicks, this, next, packet.Payload, NetworkLoggerType.Push,
            $"Push packet from network {this.Name} to {next.Name}",
            "Network", packet.Category, packet.Scope, packet.Ttl, queueLength: _monitoringPacketsQueue.Count);


        counters.CountTransfers();
        counters.CountOutbound(networkPacket.Packet);
        next.TransferNext(networkPacket);

        return Task.FromResult(true);
    }

    protected override Task ReceiveImplementationAsync(Contracts.Network.NetworkPacket packet)
    {
        return Task.CompletedTask;
    }

    //TODO: add tick reaction
    public override Task Refresh()
    {
        return Task.Run(async () =>
        {

            //var packets = _monitoringPacketsQueue.ToArray();

            foreach (var pendingPacket in _monitoringPacketsQueue)
            {
                //if (pendingPacket == null)
                //{
                //    continue;
                //}

                pendingPacket.ReduceWaitTime();

                if (pendingPacket.WaitTime > 0)
                {
                    continue;
                }
                _monitoringPacketsQueue.TryTake(out var t);
                var result = await ProcessTransferPacket(t);
                //if (_monitoringPacketsQueue.TryTake(out var pending))
                //{

                //}
            }



            //foreach (var pendingPacket in _monitoringPacketsQueue)
            //{
            //    //if (pendingPacket == null)
            //    //{
            //    //    continue;
            //    //}

            //    pendingPacket.ReduceWaitTime();

            //    if (pendingPacket.WaitTime > 0)
            //    {
            //        continue;
            //    }
            //    _monitoringPacketsQueue.TryTake(pendingPacket);
            //    var result = await ProcessTransferPacket(pendingPacket);
            //    //if (_monitoringPacketsQueue.TryTake(out var pending))
            //    //{

            //    //}
            //}
            counters.SetQueueLength(_monitoringPacketsQueue.Count);
            Counters.Refresh(NetworkGraph.TotalTicks);
        });
    }

    //TODO: need VIRTUAL wait 
    private Task<bool> ProcessTransferPacket(NetworkMonitoringPacket networkPacket)
    {
        return Task.Run(async () =>
        {
            var result = false;
            var packet = networkPacket.Packet;
            var scope = NetworkGraph.Monitoring.WithBeginScope(NetworkGraph.TotalTicks, ref packet!,
                $"Transfer packet {packet.From} to {packet.To}");

            if (networkPacket.Type == NetworkPacketType.Local)
            {
                result = await SendToLocalAsync(this, networkPacket);
            }
            else if (networkPacket.Type == NetworkPacketType.Remote)
            {
                result = await SendToRemoteAsync(networkPacket);
            }
            return result;
        });
    }

    public INode? FindNode(string address, NodeType type)
    {
        return NetworkGraph.GetNode(address, type);
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

    public override void Reset()
    {
        throw new NotImplementedException();
    }
}