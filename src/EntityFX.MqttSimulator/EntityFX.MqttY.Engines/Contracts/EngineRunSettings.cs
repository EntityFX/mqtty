namespace EntityFX.MqttY.Engines.Contracts;

/// <summary>Настройки исполнения эксперимента (из настроек Web).</summary>
public sealed class EngineRunSettings
{
    /// <summary>Лимит времени исполнения; при превышении эксперимент прерывается.</summary>
    public TimeSpan? TimeLimit { get; init; }

    /// <summary>Параллельность нескольких опытов (матрица), не распараллеливание одной модели.</summary>
    public bool ParallelExecution { get; init; }

    /// <summary>Признак запуска в отдельном процессе (для CLI-совместимого out-of-process режима).</summary>
    public bool IsolatedProcess { get; init; }
}
