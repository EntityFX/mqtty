namespace EntityFX.MqttY.Engines.Contracts;

/// <summary>
/// Контракт движка эксперимента. Каждый движок (relay, federation, loadBalancing)
/// реализует единый способ запуска, совместимый с программным вызовом из Web
/// и с CLI-совместимыми командами.
/// </summary>
public interface IExperimentEngine
{
    /// <summary>Уникальное имя движка: "relay", "federation", "loadBalancing".</summary>
    string Name { get; }

    /// <summary>Описание движка и поддерживаемых команд.</summary>
    EngineDefinition Definition { get; }

    /// <summary>Выполняет эксперимент с заданными опциями и настройками исполнения.</summary>
    Task<ExperimentResult> RunAsync(
        ExperimentRequest request,
        EngineRunSettings settings,
        CancellationToken ct);
}
