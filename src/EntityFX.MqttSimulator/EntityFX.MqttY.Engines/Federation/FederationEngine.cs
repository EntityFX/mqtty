using EntityFX.MqttY.Engines.Common;
using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Engines.Execution;
namespace EntityFX.MqttY.Engines.Federation;

/// <summary>Статическая федерация с подавлением повторов. Адаптер существующего scientific runner.</summary>
public sealed class FederationEngine : IExperimentEngine
{
    public FederationEngine(NetworkSimulationEngine engine) { }
    public string Name => "federation";
    public EngineDefinition Definition { get; } = new()
    {
        Name = "federation", Description = "Статическая федерация с подавлением повторов.",
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
