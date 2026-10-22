namespace EntityFX.MqttY.Federation;

public static class FederationGraph
{
    public static SiteLink[] Edges(FederationOptions options)
    {
        if (options.Topology is not ("line" or "ring" or "custom")) throw new InvalidDataException("Unknown topology.");
        if ((options.Topology == "custom") != (options.Links != null)) throw new InvalidDataException("Only custom topology accepts links.");
        var edges = options.Links ?? Enumerable.Range(0, Math.Max(0, options.Brokers - 1)).Select(i => new SiteLink(i, i + 1))
            .Concat(options.Topology == "ring" && options.Brokers > 2 ? new[] { new SiteLink(0, options.Brokers - 1) } : Array.Empty<SiteLink>()).ToArray();
        if (edges.Any(e => e == null || e.A < 0 || e.B < 0 || e.A >= options.Brokers || e.B >= options.Brokers || e.A == e.B) ||
            edges.Select(e => (Math.Min(e.A, e.B), Math.Max(e.A, e.B))).Distinct().Count() != edges.Length)
            throw new InvalidDataException("Invalid or duplicate physical edge.");
        var reached = new HashSet<int> { 0 };
        for (var i = 0; i < options.Brokers; i++) foreach (var e in edges)
        { if (reached.Contains(e.A)) reached.Add(e.B); if (reached.Contains(e.B)) reached.Add(e.A); }
        if (reached.Count != options.Brokers) throw new InvalidDataException("Sites must form a connected network.");
        return edges.OrderBy(e => Math.Min(e.A, e.B)).ThenBy(e => Math.Max(e.A, e.B)).ToArray();
    }
    public static int[] Neighbors(int site, SiteLink[] edges) => edges.Where(e => e.A == site || e.B == site)
        .Select(e => e.A == site ? e.B : e.A).OrderBy(x => x).ToArray();
}
