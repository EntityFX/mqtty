namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Запрос на создание и запуск симуляции.</summary>
public sealed class SimulationCreateDto
{
    public string? TopologyId { get; set; }

    public GraphDto? Graph { get; set; }

    public TopologySpecDto? Spec { get; set; }

    public string Engine { get; set; } = "relay";

    public RunSettingsDto RunSettings { get; set; } = new();
}
