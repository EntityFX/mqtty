using System.Text.Json;

namespace EntityFX.MqttY.Engines.Contracts;

/// <summary>Запрос на выполнение эксперимента. Опции передаются как JSON без разбора в CLI-слое.</summary>
public sealed class ExperimentRequest
{
    public EngineCommand Command { get; init; }

    /// <summary>Конфигурация эксперимента (JSON). Для Web — execution-представление топологии.</summary>
    public JsonElement Options { get; init; }

    /// <summary>Каталог результата; при null результаты не сохраняются (режим Demo).</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Канал снимков прогресса для мониторинга.</summary>
    public IProgress<EngineProgress>? Progress { get; init; }
}
