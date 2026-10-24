using System.Text.Json;
using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Federation;
using EntityFX.MqttY.LoadBalancing;

namespace EntityFX.MqttY.Engines.Execution;

/// <summary>Тонкий вызов существующих проверенных runners без изменения их алгоритмов.</summary>
public static class NativeExperimentAdapter
{
    public static ExperimentResult Run(SimulationRunOptions options, string? output, bool validateOnly = false,
        CancellationToken cancellationToken = default, IProgress<EngineProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reporting = System.Diagnostics.Stopwatch.StartNew();
        string? lastPhase = null;
        void Report(EntityFX.MqttY.Contracts.Network.INetworkSimulator simulator, string phase)
        {
            if (phase == lastPhase && reporting.Elapsed < TimeSpan.FromSeconds(1)) return;
            lastPhase = phase;
            reporting.Restart();
            progress?.Report(new EngineProgress { TotalTicks = simulator.TotalTicks, Step = simulator.TotalSteps,
                Errors = checked((int)simulator.Errors), Phase = phase, Nodes = NodeSnapshotBuilder.Build(simulator),
                Links = simulator.Networks.Values.SelectMany(n => n.LinkedNearestNetworks.Values.Select(other => new NetworkLinkSnapshot(n.Name, other.Name))).ToArray() });
        }
        if (options.NativeConfiguration == null)
            throw new InvalidDataException("Scientific execution requires nativeConfiguration; graph-only preview is not a federation or balancing experiment.");
        if (output == null && !validateOnly) throw new InvalidDataException("An output directory is required.");
        var native = options.NativeConfiguration.Value;
        if (options.Engine is "relay" or "federation")
        {
            var configuration = JsonSerializer.Deserialize<FederationOptions>(native.GetRawText(), FederationInput.Json)
                ?? throw new InvalidDataException("Empty federation configuration.");
            if (configuration.Mode != (options.Engine == "relay" ? FederationMode.Relay : FederationMode.Federation))
                throw new InvalidDataException("Engine/mode mismatch.");
            var input = FederationInput.Prepare(configuration);
            if (!string.Equals(input.ProfileSha256, options.ExpectedProfileSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pinned profile SHA-256 differs from the execution snapshot.");
            if (validateOnly) return new ExperimentResult { Success = true };
            var summary = new FederationRunner().Run(input, output, cancellationToken: cancellationToken, onProgress: Report);
            cancellationToken.ThrowIfCancellationRequested();
            return new ExperimentResult { Success = summary.Status == "completed", ExitCode = summary.Status == "completed" ? 0 : 1,
                Error = summary.Error, OutputDirectory = output, TotalTicks = summary.Phases.LastOrDefault()?.EndTick ?? 0 };
        }
        if (options.Engine == "loadBalancing")
        {
            var configuration = JsonSerializer.Deserialize<LoadBalancingOptions>(native.GetRawText(), ExperimentInput.Json)
                ?? throw new InvalidDataException("Empty balancing configuration.");
            var snapshot = new InputSnapshot("web-native-config.json", System.Text.Encoding.UTF8.GetBytes(native.GetRawText()));
            var profile = configuration.Ideal ? null : ProfileSnapshot.Load(configuration.BrokerProfilePath!, configuration.BrokerType);
            if (!string.Equals(profile?.Source.Sha256, options.ExpectedProfileSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pinned profile SHA-256 differs from the execution snapshot.");
            var input = ExperimentInput.Prepare(configuration, snapshot, profile);
            if (validateOnly) return new ExperimentResult { Success = true };
            var summary = new LoadBalancingExperimentRunner().Run(input, output!, cancellationToken, Report);
            cancellationToken.ThrowIfCancellationRequested();
            return new ExperimentResult { Success = summary.Status == "completed", ExitCode = summary.Status == "completed" ? 0 : 1,
                Error = summary.Error, OutputDirectory = output, TotalTicks = summary.FinalTick, Errors = summary.GraphErrors };
        }
        throw new InvalidDataException("Unknown engine.");
    }
}
