namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Связь между сетями.</summary>
public sealed class LinkDto
{
    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public int? Weight { get; set; }
}
