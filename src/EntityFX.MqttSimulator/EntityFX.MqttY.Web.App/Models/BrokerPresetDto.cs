namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Пресет двойника реального брокера.</summary>
public sealed class BrokerPresetDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string BrokerType { get; set; } = string.Empty;

    public string ProfilePath { get; set; } = string.Empty;

    public string? Description { get; set; }

    public BrokerDefaultsDto Defaults { get; set; } = new();
}
