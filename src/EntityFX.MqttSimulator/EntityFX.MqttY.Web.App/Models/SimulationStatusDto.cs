namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Статус задания симуляции.</summary>
public sealed class SimulationStatusDto
{
    public string Id { get; set; } = string.Empty;

    public string? TopologyId { get; set; }

    public string Engine { get; set; } = string.Empty;

    public string State { get; set; } = "Created";

    public long TotalTicks { get; set; }

    public long Step { get; set; }

    public long Errors { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? OutputDir { get; set; }

    public string? Error { get; set; }

    public int? ProcessId { get; set; }
    public long ProcessWorkingSetBytes { get; set; }
    public double ProcessCpuSeconds { get; set; }
    public DateTimeOffset? LastObservedAt { get; set; }
    public string? Phase { get; set; }
}
