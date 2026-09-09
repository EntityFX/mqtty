using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.Tests.Integration;

[TestClass]
public class MqttRelayExperimentTests
{
    private static MqttRelayExperimentOptions Options() => new()
    {
        Brokers = 2,
        NetworkLength = 1,
        ClientsPerBroker = 1,
        PublishQos = MqttQos.AtLeastOnce,
        SubscribeQos = MqttQos.AtLeastOnce,
        PayloadBytes = 16,
        BrokerAssignmentMode = MqttBrokerAssignmentMode.Ideal,
        WarmupTicks = 10,
        MeasurementTicks = 20,
        DrainTimeoutTicks = 2_000,
        OfferedRps = 10_000,
        RandomSeed = 42,
        Parallel = false,
        RefreshStrategy = 0,
        EnableCounters = true
    };

    [TestMethod]
    public void Options_ValidateMixedAssignmentCountAndFidelityMode()
    {
        var options = Options();
        options.BrokerAssignmentMode = MqttBrokerAssignmentMode.Mixed;
        options.MixedBrokerTypes = new[] { "Mosquitto" };
        Assert.ThrowsException<InvalidDataException>(() => options.Validate());

        options.Parallel = false;
        options.BrokerAssignmentMode = (MqttBrokerAssignmentMode)999;
        Assert.ThrowsException<InvalidDataException>(() => options.Validate());

        options.BrokerAssignmentMode = MqttBrokerAssignmentMode.Mixed;
        options.MixedBrokerTypes = new[] { "Mosquitto", "EMQX" };
        options.Validate();

        options.Parallel = true;
        options.FidelityMode = true;
        Assert.ThrowsException<InvalidDataException>(() => options.Validate());
    }

    [TestMethod]
    public void SequentialIdealExperiment_ExecutesPhasesAndExportsBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mqtty-relay-{Guid.NewGuid():N}");
        try
        {
            var ticks = new TicksOptions
            {
                TickPeriod = TimeSpan.FromMilliseconds(0.1),
                OutgoingWaitTicks = 1,
                ReceiveWaitPeriod = TimeSpan.FromSeconds(1),
                CounterHistoryDepth = 100
            };
            var network = new NetworkOptions
            {
                NetworkType = "eth",
                CapacityBytesPerSecond = 100_000_000,
                QueueCapacity = 100_000,
                ThroughputWindowTicks = 100,
                TransferTicks = 1
            };
            var runner = new MqttRelayExperimentRunner(ticks, network);
            var result = runner.Run(Options(), root,
                new ExperimentProvenance("mqtt-y-sha", "benchmark-sha", "profile-sha"));

            Assert.AreEqual(ExperimentRunStatus.Completed, result.Summary.Status, result.Summary.Error);
            Assert.AreEqual(20L,
                result.Summary.MeasurementEndTick - result.Summary.MeasurementStartTick);
            Assert.IsTrue(result.Summary.Simulated.Attempted > 0);
            foreach (var file in new[]
            {
                "experiment-manifest.json", "summary.json", "counters.json",
                "latency-histogram.json", "topology.graphml", "topology.puml"
            })
                Assert.IsTrue(File.Exists(Path.Combine(result.RunDirectory, file)), file);

            using var summary = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(result.RunDirectory, "summary.json")));
            Assert.AreEqual("completed", summary.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void SequentialIdealExperiment_WithSameSeedProducesSameSummary()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mqtty-relay-{Guid.NewGuid():N}");
        try
        {
            var ticks = TestTicks();
            var network = TestNetwork();
            var provenance = new ExperimentProvenance("mqtt-y-sha", "benchmark-sha", "profile-sha");
            var first = new MqttRelayExperimentRunner(ticks, network).Run(Options(), root, provenance);
            var second = new MqttRelayExperimentRunner(ticks, network).Run(Options(), root, provenance);

            Assert.AreEqual(JsonSerializer.Serialize(first.Summary),
                JsonSerializer.Serialize(second.Summary));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static TicksOptions TestTicks() => new()
    {
        TickPeriod = TimeSpan.FromMilliseconds(0.1),
        OutgoingWaitTicks = 1,
        ReceiveWaitPeriod = TimeSpan.FromSeconds(1),
        CounterHistoryDepth = 100
    };

    private static NetworkOptions TestNetwork() => new()
    {
        NetworkType = "eth",
        CapacityBytesPerSecond = 100_000_000,
        QueueCapacity = 100_000,
        ThroughputWindowTicks = 100,
        TransferTicks = 1
    };
}
