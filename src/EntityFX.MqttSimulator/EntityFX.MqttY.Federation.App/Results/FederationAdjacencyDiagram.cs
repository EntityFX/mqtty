using System.Text;
namespace EntityFX.MqttY.Federation;

public static class FederationAdjacencyDiagram
{
    public static string Svg(FederationOptions options, (int A, int B, string Label)[] edges)
    {
        var routes = edges.Select(e => (e.A, e.B)).ToHashSet();
        if (options.Mode == FederationMode.Single) foreach (var edge in edges) routes.Add((edge.B, edge.A));
        const int cell = 48, left = 130, top = 100;
        var width = left + options.Brokers * cell + 60; var height = top + options.Brokers * cell + 110;
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {width} {height}\"><rect width=\"{width}\" height=\"{height}\" fill=\"white\"/><g font-family=\"sans-serif\" text-anchor=\"middle\">");
        svg.Append($"<text x=\"{width / 2}\" y=\"28\" font-size=\"20\">Логическая сеть {options.Mode}: {routes.Count} направлений</text>");
        svg.Append($"<text x=\"{width / 2}\" y=\"54\" font-size=\"13\">Строка — источник; столбец — назначение</text>");
        for (var i = 0; i < options.Brokers; i++)
        {
            svg.Append($"<text x=\"{left - 30}\" y=\"{top + i * cell + 29}\" font-size=\"14\">S{i}</text>");
            svg.Append($"<text x=\"{left + i * cell + 24}\" y=\"{top - 14}\" font-size=\"14\">S{i}</text>");
            for (var j = 0; j < options.Brokers; j++)
            {
                var linked = routes.Contains((i, j));
                svg.Append($"<rect x=\"{left + j * cell}\" y=\"{top + i * cell}\" width=\"46\" height=\"46\" fill=\"{(linked ? "#2878b5" : "#edf1f5")}\"/>");
                svg.Append($"<text x=\"{left + j * cell + 23}\" y=\"{top + i * cell + 29}\" font-size=\"18\" fill=\"{(linked ? "white" : "#89929c")}\">{(linked ? "→" : "—")}</text>");
            }
        }
        svg.Append($"<text x=\"{width / 2}\" y=\"{height - 58}\" font-size=\"13\">S — площадка. Локальная доставка не входит в число направлений.</text>");
        svg.Append($"<text x=\"{width / 2}\" y=\"{height - 32}\" font-size=\"13\">{(options.Topology == "star" ? "Межплощадочные MQTT-пути проходят через узел transit." : "MQTT-пути проходят по заданной физической сети.")}</text></g></svg>");
        return svg.ToString();
    }
}
