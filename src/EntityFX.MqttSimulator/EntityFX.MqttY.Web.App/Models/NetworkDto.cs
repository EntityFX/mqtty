namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Сеть в графе.</summary>
public sealed class NetworkDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string NetworkType { get; set; } = "ethernet";

    public double X { get; set; }

    public double Y { get; set; }
}
