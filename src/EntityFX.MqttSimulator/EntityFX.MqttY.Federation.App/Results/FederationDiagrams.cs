using System.Globalization;
using System.Text;
using System.Xml.Linq;
namespace EntityFX.MqttY.Federation;

public static class FederationDiagrams
{
    public static void Verify(string graphMl, FederationOptions o)
    {
        var doc = XDocument.Parse(graphMl); XNamespace ns = "http://graphml.graphdrawing.org/xmlns";
        var nodes = doc.Descendants(ns + "node").ToDictionary(e => (string)e.Attribute("id")!, e => e.Elements(ns + "data")
            .ToDictionary(d => (string)d.Attribute("key")!, d => d.Value));
        var networks = nodes.Where(n => n.Value["type"] == "Network").ToDictionary(n => n.Key, n => n.Value["label"]);
        if (!networks.Values.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(Enumerable.Range(0, o.Brokers).Select(i => "site" + i).OrderBy(x => x, StringComparer.Ordinal)))
            throw new InvalidDataException("Exported sites differ from the diagram.");
        var edges = doc.Descendants(ns + "edge").Select(e => ((string)e.Attribute("source")!, (string)e.Attribute("target")!)).ToArray();
        var actual = edges.Where(e => networks.ContainsKey(e.Item1) && networks.ContainsKey(e.Item2))
            .Select(e => Pair(networks[e.Item1], networks[e.Item2])).ToHashSet();
        var expected = FederationGraph.Edges(o).Select(e => Pair("site" + e.A, "site" + e.B)).ToHashSet();
        if (!actual.SetEquals(expected)) throw new InvalidDataException("Exported physical edges differ from the diagram.");
        VerifyClients("publisher", Enumerable.Range(0, o.Publishers).Select(p => ($"publisher{p:D4}", o.PublisherSite(p))).ToArray());
        VerifyClients("receiver", o.Receivers().Select(s => ($"receiver{s:D4}", s)).ToArray());
        var brokers = nodes.Values.Where(n => n["type"] == "Server").Select(n => n["label"]).OrderBy(s => s).ToArray();
        if (!brokers.SequenceEqual(Enumerable.Range(0, o.Mode == FederationMode.Single ? 1 : o.Brokers).Select(i => $"broker{i:D4}")))
            throw new InvalidDataException("Exported brokers differ from the diagram.");
        void VerifyClients(string prefix, (string Name, int Site)[] clients)
        {
            if (nodes.Values.Count(n => n["type"] == "Client" && n["label"].StartsWith(prefix, StringComparison.Ordinal)) != clients.Length)
                throw new InvalidDataException("Exported client population differs.");
            foreach (var client in clients)
            {
                var id = nodes.Single(n => n.Value["type"] == "Client" && n.Value["label"] == client.Name).Key;
                var attachments = edges.Where(e => e.Item1 == id && networks.ContainsKey(e.Item2) || e.Item2 == id && networks.ContainsKey(e.Item1))
                    .Select(e => networks[e.Item1 == id ? e.Item2 : e.Item1]).Distinct().ToArray();
                if (attachments.Length != 1 || attachments[0] != "site" + client.Site) throw new InvalidDataException("Client moved from its declared physical site.");
            }
        }
    }
    private static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "/" + b : b + "/" + a;
    public static void Write(string output, FederationOptions o, string graphMl)
    {
        Verify(graphMl, o);
        foreach (var logical in new[] { false, true })
        {
            var kind = logical ? "logical" : "physical";
            var mmd = new StringBuilder("flowchart LR\n");
            for (var i = 0; i < o.Brokers; i++)
            {
                mmd.AppendLine($"  S{i}[\"Площадка {i}: publishers={Enumerable.Range(0, o.Publishers).Count(p => o.PublisherSite(p) == i)}; subscribers={(o.Receivers().Contains(i) ? 1 : 0)}; брокер={(o.Mode != FederationMode.Single || i == 0 ? "есть" : "нет")}\"]");
            }
            foreach (var (a, b, label) in Edges(o, logical)) mmd.AppendLine($"  S{a} -->|\"{label}\"| S{b}");
            File.WriteAllText(Path.Combine(output, $"topology-{kind}.mmd"), mmd.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, $"topology-{kind}.svg"), Svg(o, logical), new UTF8Encoding(false));
        }
    }
    private static IEnumerable<(int A, int B, string Label)> Edges(FederationOptions o, bool logical)
    {
        var edges = FederationGraph.Edges(o);
        if (!logical) return edges.Select(e => (e.A, e.B, "сеть ↔"));
        if (o.Mode == FederationMode.Single) return Enumerable.Range(1, o.Brokers - 1).Select(s => (s, 0, "MQTT к B0 ↔"));
        if (o.Mode == FederationMode.Federation) return edges.SelectMany(e => new[] { (e.A, e.B, "federator"), (e.B, e.A, "federator") });
        return Enumerable.Range(0, o.Brokers).SelectMany(origin => FederationGraph.Tree(origin, o.Brokers, edges).Select(e => (e.A, e.B, $"источник {origin}")));
    }
    private static string Svg(FederationOptions o, bool logical)
    {
        static string N(double n) => n.ToString("0.###", CultureInfo.InvariantCulture);
        var positions = Enumerable.Range(0, o.Brokers).Select(i => (X: 450 + 300 * Math.Cos(2 * Math.PI * i / o.Brokers - Math.PI / 2), Y: 340 + 220 * Math.Sin(2 * Math.PI * i / o.Brokers - Math.PI / 2))).ToArray();
        var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 900 690\"><defs><marker id=\"arrow\" markerWidth=\"10\" markerHeight=\"10\" refX=\"9\" refY=\"3\" orient=\"auto-start-reverse\"><path d=\"M0,0 L0,6 L9,3 z\" fill=\"#566578\"/></marker></defs><rect width=\"900\" height=\"690\" fill=\"white\"/>");
        svg.Append($"<text x=\"450\" y=\"30\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"21\">{(logical ? "Логические соединения" : "Физическая сеть")}: {o.Mode}</text>");
        var color = new[] { "#2878b5", "#d16b32", "#33864b", "#9856a2" };
        var edgeIndex = 0;
        foreach (var e in Edges(o, logical))
        {
            var a = positions[e.A]; var b = positions[e.B]; var dx = b.X - a.X; var dy = b.Y - a.Y; var length = Math.Sqrt(dx * dx + dy * dy); var offset = o.Mode == FederationMode.Relay && logical ? (edgeIndex % 3 - 1) * 5 : 0;
            var x1 = a.X + dx / length * 72 - dy / length * offset; var y1 = a.Y + dy / length * 52 + dx / length * offset;
            var x2 = b.X - dx / length * 72 - dy / length * offset; var y2 = b.Y - dy / length * 52 + dx / length * offset;
            svg.Append($"<line x1=\"{N(x1)}\" y1=\"{N(y1)}\" x2=\"{N(x2)}\" y2=\"{N(y2)}\" stroke=\"{color[edgeIndex % color.Length]}\" stroke-width=\"2\" marker-end=\"url(#arrow)\"{(!logical || o.Mode == FederationMode.Single ? " marker-start=\"url(#arrow)\"" : "")}/>");
            if (o.Mode == FederationMode.Relay && logical) svg.Append($"<text x=\"{N((x1 + x2) / 2)}\" y=\"{N((y1 + y2) / 2 - 8 - offset * 2)}\" font-family=\"sans-serif\" font-size=\"11\">{e.Label}</text>");
            edgeIndex++;
        }
        for (var i = 0; i < o.Brokers; i++)
        {
            var p = positions[i];
            svg.Append($"<rect x=\"{N(p.X - 90)}\" y=\"{N(p.Y - 43)}\" width=\"180\" height=\"86\" rx=\"10\" fill=\"#eef4fb\" stroke=\"#435a72\"/>");
            var lines = new[] { $"Площадка {i}", $"P={Enumerable.Range(0, o.Publishers).Count(n => o.PublisherSite(n) == i)}; S={(o.Receivers().Contains(i) ? 1 : 0)}", o.Mode != FederationMode.Single || i == 0 ? $"Брокер B{i}" : "Клиенты → B0" };
            for (var row = 0; row < lines.Length; row++) svg.Append($"<text x=\"{N(p.X)}\" y=\"{N(p.Y - 20 + row * 23)}\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"15\">{lines[row]}</text>");
        }
        svg.Append("<text x=\"450\" y=\"640\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"14\">P — publishers; S — subscribers. Расположение клиентов неизменно.</text>");
        svg.Append($"<text x=\"450\" y=\"666\" text-anchor=\"middle\" font-family=\"sans-serif\" font-size=\"13\">{(logical ? "Стрелки — MQTT-соединения; локальная доставка показана счётчиками S." : "Связи двусторонние; логические MQTT-пути могут проходить через несколько площадок.")}</text></svg>");
        return svg.ToString();
    }
}
