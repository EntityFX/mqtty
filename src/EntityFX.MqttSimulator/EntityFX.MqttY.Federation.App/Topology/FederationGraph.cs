namespace EntityFX.MqttY.Federation;

public static class FederationGraph
{
    public static int NetworkCount(FederationOptions options) => options.PhysicalLayout?.NetworkNames.Length ?? options.Brokers + (options.Topology == "star" ? 1 : 0);
    public static string NetworkName(FederationOptions options, int index) => options.PhysicalLayout?.NetworkNames[index] ?? (index == options.Brokers && options.Topology == "star" ? "transit" : "site" + index);

    public static SiteLink[] Edges(FederationOptions options)
    {
        if (options.Topology is not ("line" or "ring" or "star" or "custom")) throw new InvalidDataException("Unknown topology.");
        if ((options.Topology == "custom") != (options.Links != null)) throw new InvalidDataException("Only custom topology accepts links.");
        if (options.Topology == "star") return Enumerable.Range(0, options.Brokers).Select(site => new SiteLink(site, options.Brokers)).ToArray();
        var edges = options.Links ?? Enumerable.Range(0, Math.Max(0, options.Brokers - 1)).Select(i => new SiteLink(i, i + 1))
            .Concat(options.Topology == "ring" && options.Brokers > 2 ? new[] { new SiteLink(0, options.Brokers - 1) } : Array.Empty<SiteLink>()).ToArray();
        if (edges.Any(e => e == null || e.A < 0 || e.B < 0 || e.A >= NetworkCount(options) || e.B >= NetworkCount(options) || e.A == e.B) ||
            edges.Select(e => (Math.Min(e.A, e.B), Math.Max(e.A, e.B))).Distinct().Count() != edges.Length)
            throw new InvalidDataException("Invalid or duplicate physical edge.");
        var reached = new HashSet<int> { 0 };
        for (var i = 0; i < NetworkCount(options); i++) foreach (var e in edges)
        { if (reached.Contains(e.A)) reached.Add(e.B); if (reached.Contains(e.B)) reached.Add(e.A); }
        if (reached.Count != NetworkCount(options)) throw new InvalidDataException("Sites must form a connected network.");
        return edges.OrderBy(e => Math.Min(e.A, e.B)).ThenBy(e => Math.Max(e.A, e.B)).ToArray();
    }
    public static int[] Neighbors(int site, SiteLink[] edges) => edges.Where(e => e.A == site || e.B == site)
        .Select(e => e.A == site ? e.B : e.A).OrderBy(x => x).ToArray();

    // The transit network has no MQTT endpoint. Brokers on its shared network
    // can establish ordinary MQTT connections to each other through that node.
    public static SiteLink[] ForwardingEdges(FederationOptions options)
    {
        if (options.PhysicalLayout == null)
            return options.Topology == "star"
                ? Enumerable.Range(0, options.Brokers).SelectMany(a => Enumerable.Range(a + 1, options.Brokers - a - 1).Select(b => new SiteLink(a, b))).ToArray()
                : Edges(options);
        var edges = Edges(options);
        var brokerAt = options.PhysicalLayout.BrokerNetworks.Select((network, site) => (network, site)).ToDictionary(x => x.network, x => x.site);
        var result = new HashSet<(int, int)>();
        // Traverse transit networks; the next broker is a forwarding neighbor.
        for (var site = 0; site < options.Brokers; site++)
        {
            var pending = new Queue<int>();var seen = new HashSet<int>();pending.Enqueue(options.NetworkForSite(site));
            while (pending.TryDequeue(out var network))
            {
                if (!seen.Add(network)) continue;
                if (brokerAt.TryGetValue(network, out var other) && other != site)
                { result.Add((Math.Min(site, other), Math.Max(site, other)));continue; }
                foreach (var next in Neighbors(network, edges)) pending.Enqueue(next);
            }
        }
        return result.OrderBy(e => e.Item1).ThenBy(e => e.Item2).Select(e => new SiteLink(e.Item1, e.Item2)).ToArray();
    }
}
