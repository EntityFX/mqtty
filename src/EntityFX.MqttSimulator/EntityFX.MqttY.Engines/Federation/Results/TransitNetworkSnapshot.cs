using EntityFX.MqttY.Contracts.Counters;
namespace EntityFX.MqttY.Federation;

public sealed record TransitNetworkSnapshot(long Tick, long QueuedPackets, IReadOnlyDictionary<string, long> Counters)
{
    public static TransitNetworkSnapshot Capture(FederationTopology topology)
    {
        var network = topology.Graph.Networks["transit"];
        var group = network.Counters.Counters.OfType<CounterGroup>().Single(c => c.GroupType == "NetworkCounters");
        var counters = group.Counters.Where(c => c.Name is "TransferPackets" or "InboundPackets" or "OutboundPackets" or "Inbound" or "Outbound" or "Refused")
            .OrderBy(c => c.Name, StringComparer.Ordinal).ToDictionary(c => c.Name, c => Convert.ToInt64(c.Value));
        return new(topology.Graph.TotalTicks, network.QueueSize, counters);
    }
}
