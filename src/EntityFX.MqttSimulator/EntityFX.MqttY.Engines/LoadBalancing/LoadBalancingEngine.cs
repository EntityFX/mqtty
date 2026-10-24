using EntityFX.MqttY.Engines.Common;
using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Engines.Execution;
namespace EntityFX.MqttY.Engines.LoadBalancing;

/// <summary>Балансировка постоянных MQTT-соединений. Адаптер существующего scientific runner.</summary>
public sealed class LoadBalancingEngine : IExperimentEngine
{
    public LoadBalancingEngine(NetworkSimulationEngine engine) { }
    public string Name => "loadBalancing";
    public EngineDefinition Definition { get; } = new()
    {
        Name = "loadBalancing", Description = "Балансировка постоянных MQTT-соединений.",
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
