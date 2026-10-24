namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Снимок мониторинга симуляции.</summary>
public sealed class MonitoringSnapshotDto
{
    public string SimulationId { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public long TotalTicks { get; set; }

    public long Step { get; set; }

    public long Errors { get; set; }
    public string? Phase { get; set; }

    public IReadOnlyList<NodeStatusDto> Nodes { get; set; } = Array.Empty<NodeStatusDto>();
    public IReadOnlyList<LinkDto> Links { get; set; } = Array.Empty<LinkDto>();
}
