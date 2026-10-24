namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Узел в графе.</summary>
public sealed class NodeDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Type { get; set; } = "Client";

    public string Specification { get; set; } = string.Empty;

    public string? Network { get; set; }

    public string? Broker { get; set; }
    public string? ConnectsToServer { get; set; }
    public object? Configuration { get; set; }
    public Dictionary<string, string[]> Additional { get; set; } = new();

    public double X { get; set; }

    public double Y { get; set; }
}
