namespace EntityFX.MqttY.Federation;

public sealed record TransitNetworkEvidence(string Name, int NetworkCount, int BrokerCount, bool HasAttachedEndpoints,
    TransitNetworkSnapshot? BeforeMeasurement, TransitNetworkSnapshot Final,
    IReadOnlyDictionary<string, long>? MeasurementAndDrainCounters, NetworkPathResult[] AvailablePaths)
{
    public static TransitNetworkEvidence Capture(FederationOptions options, FederationTopology topology, TransitNetworkSnapshot? baseline)
    {
        var graph = topology.Graph; var transit = graph.Networks["transit"];
        var final = TransitNetworkSnapshot.Capture(topology);
        var routes = (from source in Enumerable.Range(0, options.Brokers)
                      from target in Enumerable.Range(0, options.Brokers)
                      where source != target
                      select new NetworkPathResult(source, target, new[] { "site" + source }.Concat(graph.PathFinder
                          .GetPath(graph.Networks["site" + source], graph.Networks["site" + target]).Select(n => n.Name)).ToArray())).ToArray();
        if (routes.Any(r => !r.Networks.SequenceEqual(new[] { "site" + r.Source, "transit", "site" + r.Target })))
            throw new InvalidDataException("The physical path bypasses the transit network.");
        return new("transit", graph.Networks.Count, topology.Brokers.Length,
            transit.Clients.Count != 0 || transit.Servers.Count != 0 || transit.Applications.Count != 0,
            baseline, final, baseline == null ? null : final.Counters.ToDictionary(kv => kv.Key, kv => kv.Value - baseline.Counters[kv.Key]), routes);
    }
}
