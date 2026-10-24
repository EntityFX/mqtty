using EntityFX.MqttY.Engines.Contracts;

namespace EntityFX.MqttY.Engines.Hosting;

/// <summary>
/// Единая точка входа для запуска движков: реестр по имени и выполнение запроса.
/// Используется и Web (in-process), и CLI-совместимыми обёртками.
/// </summary>
public sealed class EngineHost
{
    private readonly IReadOnlyDictionary<string, IExperimentEngine> _engines;

    public EngineHost(IEnumerable<IExperimentEngine> engines)
    {
        _engines = engines.ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<EngineDefinition> Definitions =>
        _engines.Values.Select(e => e.Definition).OrderBy(d => d.Name).ToList();

    public IExperimentEngine? Get(string name) =>
        _engines.GetValueOrDefault(name);

    public async Task<ExperimentResult> RunAsync(
        string engineName,
        ExperimentRequest request,
        EngineRunSettings settings,
        CancellationToken ct)
    {
        if (!_engines.TryGetValue(engineName, out var engine))
        {
            return new ExperimentResult
            {
                Success = false,
                ExitCode = 1,
                Error = $"Unknown engine '{engineName}'."
            };
        }

        return await engine.RunAsync(request, settings, ct);
    }
}
