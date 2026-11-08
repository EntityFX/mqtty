using System.Collections.Concurrent;
using System.Collections.Generic;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

/// <summary>
/// Owns the topology registries and publishes an immutable, ordered snapshot.
/// A snapshot is rebuilt only when the topology version changes; refresh loops
/// therefore never sort or copy the dictionaries on a normal tick.
/// </summary>
public sealed class TopologyRegistry
{
    private readonly ConcurrentDictionary<(string Address, NodeType NodeType), ILeafNode> _nodes = new();
    private readonly ConcurrentDictionary<string, INetwork> _networks = new();
    private readonly ConcurrentDictionary<string, IClient> _clients = new();
    private readonly ConcurrentDictionary<string, IServer> _servers = new();
    private readonly ConcurrentDictionary<string, IApplication> _apps = new();

    private long _version;
    private int _countNodes;
    private TopologySnapshot _snapshot = TopologySnapshot.Empty;

    public IDictionary<string, INetwork> Networks => _networks;

    public IDictionary<string, IClient> Clients => _clients;

    public IDictionary<string, IServer> Servers => _servers;

    public IDictionary<string, IApplication> Applications => _apps;

    internal ConcurrentDictionary<(string Address, NodeType NodeType), ILeafNode> Nodes => _nodes;

    internal ConcurrentDictionary<string, INetwork> NetworkMap => _networks;

    internal ConcurrentDictionary<string, IClient> ClientMap => _clients;

    internal ConcurrentDictionary<string, IServer> ServerMap => _servers;

    internal ConcurrentDictionary<string, IApplication> ApplicationMap => _apps;

    public long Version => Volatile.Read(ref _version);

    public int CountNodes => Volatile.Read(ref _countNodes);

    public ILeafNode? GetNode(string address, NodeType nodeType) =>
        _nodes.TryGetValue((address, nodeType), out var node) ? node : null;

    public bool ContainsNode(string address, NodeType nodeType) =>
        _nodes.ContainsKey((address, nodeType));

    public void MarkChanged() => Interlocked.Increment(ref _version);

    public void AddNodeCount() => Interlocked.Increment(ref _countNodes);

    public void RemoveNodeCount() => Interlocked.Decrement(ref _countNodes);

    /// <summary>
    /// Returns the ordered snapshot for the current topology version, rebuilding
    /// it only when a mutation advanced the version.
    /// </summary>
    public TopologySnapshot GetSnapshot()
    {
        var version = Version;
        var snapshot = Volatile.Read(ref _snapshot);
        if (snapshot.Version == version)
        {
            return snapshot;
        }

        var networks = _networks
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        var nodes = _nodes
            .OrderBy(item => item.Key.Address, StringComparer.Ordinal)
            .ThenBy(item => item.Key.NodeType)
            .ToArray();

        var next = new TopologySnapshot(version, networks, nodes);
        Volatile.Write(ref _snapshot, next);
        return next;
    }
}
