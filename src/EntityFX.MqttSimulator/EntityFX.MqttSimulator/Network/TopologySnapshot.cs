using System.Collections.Generic;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Network;

/// <summary>
/// Immutable view of the topology for one refresh tick. The arrays are ordered
/// exactly like the old per-tick ordering (network name; node address then type),
/// which is what makes schedule-independent behaviour reproducible.
/// </summary>
public sealed class TopologySnapshot
{
    public static readonly TopologySnapshot Empty = new(
        0,
        Array.Empty<KeyValuePair<string, INetwork>>(),
        Array.Empty<KeyValuePair<(string Address, NodeType NodeType), ILeafNode>>());

    public TopologySnapshot(
        long version,
        KeyValuePair<string, INetwork>[] networks,
        KeyValuePair<(string Address, NodeType NodeType), ILeafNode>[] nodes)
    {
        Version = version;
        Networks = networks;
        Nodes = nodes;
    }

    public long Version { get; }

    public KeyValuePair<string, INetwork>[] Networks { get; }

    public KeyValuePair<(string Address, NodeType NodeType), ILeafNode>[] Nodes { get; }
}
