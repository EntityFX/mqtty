using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Mqtt.Diagnostics;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Network.Diagnostics;

namespace EntityFX.MqttY.Federation;

/// <summary>Read-only bounded snapshots at phase boundaries, never a model scheduler.</summary>
public sealed class FederationDrainDiagnostics
{
    public int SchemaVersion => 1;
    public int PendingDetailLimitPerNode => 32;
    public int SessionDetailLimitPerNode => 64;
    public string PendingAge => "unavailable: existing pending state has no created tick";
    public List<FederationDrainSnapshot> Snapshots { get; init; } = new();

    public void Capture(FederationTopology topology, string phase, string occurrence)
    {
        var networks = topology.Graph.Networks.Values.OrderBy(n => n.Name, StringComparer.Ordinal).Select(n =>
        {
            var counters = n.Counters.Counters.OfType<CounterGroup>().Where(g => g.GroupType == "NetworkCounters")
                .SelectMany(g => g.Counters)
                .Where(c => c.Name is "Refused" or "InboundPackets" or "OutboundPackets" or "TransferPackets" or "Inbound" or "Outbound")
                .OrderBy(c => c.Name, StringComparer.Ordinal).ToDictionary(c => c.Name, c => Convert.ToInt64(c.Value));
            return new FederationNetworkSnapshot(n.Name, n.QueueSize, counters);
        }).ToArray();
        var nodes = topology.Graph.Networks.Values.SelectMany(n => n.Nodes.Values)
            .OfType<INodeQueueDiagnostics>().Cast<INode>().OrderBy(n => n.Name, StringComparer.Ordinal)
            .Select(n => new FederationNodeSnapshot(n.Name, ((IQuiescenceParticipant)n).IsQuiescent,
                ((INodeQueueDiagnostics)n).GetQueueSnapshot(),
                (n as IMqttPendingDiagnostics)?.GetPendingSnapshot(PendingDetailLimitPerNode, SessionDetailLimitPerNode))).ToArray();
        Snapshots.Add(new(phase, occurrence, topology.Graph.TotalTicks, topology.IsIdle, networks, nodes,
            topology.Federators.SelectMany(f => f.GetQueueSnapshots()).ToArray()));
    }
}
