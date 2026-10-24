using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Engines.Execution;

/// <summary>
/// Execution-представление топологии: дизайн-граф (NetworkGraphOption) плюс параметры
/// исполнения (фазы warmup/measurement/drain, параллельность, стратегия refresh).
/// </summary>
public sealed class SimulationRunOptions
{
    public NetworkGraphOption Graph { get; set; } = new();

    public string Engine { get; set; } = "relay";

    public long WarmupTicks { get; set; } = 50000;

    public long MeasurementTicks { get; set; } = 300000;

    public long DrainTimeoutTicks { get; set; } = 100000;

    public bool Parallel { get; set; }

    public int RefreshStrategy { get; set; }

    /// <summary>Проверенная конфигурация существующего научного движка, а не граф-заглушка.</summary>
    public System.Text.Json.JsonElement? NativeConfiguration { get; set; }
    public string? ExpectedProfileSha256 { get; set; }
}
