using EntityFX.MqttY.Engines.Execution;
using Models = EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
using Xunit;

namespace EntityFX.Tests.Web;

public sealed class NetworkSimulationEngineTests
{
    [Fact]
    public async Task RunAsync_SmallLine_CompletesAndWritesResults()
    {
        var generator = new TopologyGeneratorService();
        var spec = new Models.TopologySpecDto
        {
            Kind = "line",
            ChainLength = 2,
            BrokerPresetId = "Mosquitto",
            ClientsPerBroker = 1,
            Engine = "relay",
            WarmupTicks = 10,
            MeasurementTicks = 20,
            DrainTimeoutTicks = 10
        };
        var graph = generator.Generate(spec);

        var outputDir = Path.Combine(Path.GetTempPath(), $"mqtty-engine-test-{Guid.NewGuid():N}");
        var engine = new NetworkSimulationEngine();
        var result = await engine.RunAsync(
            new SimulationRunOptions
            {
                Graph = graph,
                Engine = "relay",
                WarmupTicks = 10,
                MeasurementTicks = 20,
                DrainTimeoutTicks = 10
            },
            outputDir,
            progress: null,
            timeLimit: TimeSpan.FromSeconds(30),
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.True(result.TotalTicks > 0);
        Assert.True(File.Exists(Path.Combine(outputDir, "summary.json")));
        Assert.True(File.Exists(Path.Combine(outputDir, "experiment-manifest.json")));

        try
        {
            Directory.Delete(outputDir, recursive: true);
        }
        catch
        {
            // временный каталог
        }
    }

    [Fact]
    public async Task RunAsync_TimeLimit_ReturnsTimedOut()
    {
        var generator = new TopologyGeneratorService();
        var spec = new Models.TopologySpecDto
        {
            Kind = "line",
            ChainLength = 2,
            BrokerPresetId = "Mosquitto",
            ClientsPerBroker = 1,
            Engine = "relay",
            WarmupTicks = 100000,
            MeasurementTicks = 100000,
            DrainTimeoutTicks = 100000
        };
        var graph = generator.Generate(spec);

        var engine = new NetworkSimulationEngine();
        var result = await engine.RunAsync(
            new SimulationRunOptions
            {
                Graph = graph,
                Engine = "relay",
                WarmupTicks = 100000,
                MeasurementTicks = 100000,
                DrainTimeoutTicks = 100000
            },
            outputDirectory: null,
            progress: null,
            timeLimit: TimeSpan.FromMilliseconds(50),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(2, result.ExitCode);
    }
}
