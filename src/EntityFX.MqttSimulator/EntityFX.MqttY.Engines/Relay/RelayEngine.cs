using EntityFX.MqttY.Engines.Common;
using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Engines.Execution;
namespace EntityFX.MqttY.Engines.Relay;

/// <summary>Проверенный MqttRelay, каждый брокер со всеми остальными. Адаптер существующего scientific runner.</summary>
public sealed class RelayEngine : IExperimentEngine
{
    public RelayEngine(NetworkSimulationEngine engine) { }
    public string Name => "relay";
    public EngineDefinition Definition { get; } = new()
    {
        Name = "relay", Description = "Проверенный MqttRelay, каждый брокер со всеми остальными.",
        Commands = new[] { EngineCommand.Run, EngineCommand.Validate, EngineCommand.Demo }
    };
    public Task<ExperimentResult> RunAsync(ExperimentRequest request, EngineRunSettings settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var options = SimulationOptionsParser.Parse(request.Options);
        options.Engine = Name;
        return Task.FromResult(NativeExperimentAdapter.Run(options, request.OutputDirectory, request.Command == EngineCommand.Validate, ct, request.Progress));
    }
}
