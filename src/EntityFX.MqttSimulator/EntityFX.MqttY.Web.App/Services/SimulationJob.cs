using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Engines.Execution;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Задание симуляции в реестре.</summary>
public sealed class SimulationJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string? TopologyId { get; set; }

    public string Engine { get; set; } = "relay";

    public string State { get; set; } = "Created";

    public long TotalTicks { get; set; }

    public long Step { get; set; }

    public long Errors { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public string? OutputDir { get; set; }

    public string? Error { get; set; }

    public int? ProcessId { get; set; }

    public SimulationRunOptions RunOptions { get; set; } = new();

    public TimeSpan? TimeLimit { get; set; }

    public bool Parallel { get; set; }

    public string ExecutionMode { get; set; } = "InProcess";

    [System.Text.Json.Serialization.JsonIgnore]
    public CancellationTokenSource? Cts { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public IProgress<EngineProgress>? Progress { get; set; }

    public long ProcessWorkingSetBytes { get; set; }
    public double ProcessCpuSeconds { get; set; }
    public DateTimeOffset? LastObservedAt { get; set; }
    public string? Phase { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public object SyncRoot { get; } = new();
}
