namespace EntityFX.MqttY.Engines.Contracts;

/// <summary>Результат выполнения эксперимента.</summary>
public sealed class ExperimentResult
{
    public bool Success { get; init; }

    public int ExitCode { get; init; }

    public string? OutputDirectory { get; init; }

    public string? Error { get; init; }

    public long TotalTicks { get; init; }

    public long TotalSteps { get; init; }

    public long Errors { get; init; }
}
