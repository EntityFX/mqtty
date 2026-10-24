using System.Text.Json;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;

namespace EntityFX.MqttY.Engines.Execution;

/// <summary>
/// Сохраняет результаты симуляции в каталог результата в формате, совместимом с CLI:
/// experiment-manifest.json, summary.json, counters.json.
/// </summary>
public sealed class SimulationResultWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _outputDirectory;

    public SimulationResultWriter(string outputDirectory)
    {
        _outputDirectory = outputDirectory;
    }

    public void Write(NetworkSimulator simulator, SimulationRunOptions options, bool timedOut, bool failed)
    {
        Directory.CreateDirectory(_outputDirectory);

        var manifest = new
        {
            schemaVersion = 1,
            engine = options.Engine,
            graph = new
            {
                networks = options.Graph.Networks.Count,
                nodes = options.Graph.Nodes.Count,
                enableCounters = options.Graph.EnableCounters,
                randomSeed = options.Graph.RandomSeed
            },
            phases = new
            {
                warmupTicks = options.WarmupTicks,
                measurementTicks = options.MeasurementTicks,
                drainTimeoutTicks = options.DrainTimeoutTicks,
                parallel = options.Parallel,
                refreshStrategy = options.RefreshStrategy
            },
            timedOut,
            failed
        };

        var summary = new
        {
            success = !timedOut && !failed,
            exitCode = timedOut ? 2 : failed ? 1 : 0,
            totalTicks = simulator.TotalTicks,
            totalSteps = simulator.TotalSteps,
            errors = simulator.Errors,
            virtualTime = simulator.VirtualTime,
            realTime = simulator.RealTime
        };

        var counters = NodeSnapshotBuilder.Build(simulator)
            .ToDictionary(n => n.Id, n => n.Counters);

        WriteJson("experiment-manifest.json", manifest);
        WriteJson("summary.json", summary);
        WriteJson("counters.json", counters);
    }

    private void WriteJson(string fileName, object value)
    {
        var path = Path.Combine(_outputDirectory, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }
}
