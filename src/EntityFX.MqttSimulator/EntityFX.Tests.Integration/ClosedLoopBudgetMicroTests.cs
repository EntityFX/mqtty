using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.Tests.Integration;

[TestClass]
public class ClosedLoopBudgetMicroTests
{
    [DataTestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void NonbindingCeiling_DoesNotChangeIdealClosedLoopResults(int qos)
    {
        // These are technical simulations, not a comparison with a real profile.
        var root = Environment.GetEnvironmentVariable("MQTTY_ARTICLE1_MICRO_OUTPUT")
            ?? Path.Combine(Path.GetTempPath(), "mqtty-article1-micro-" + Guid.NewGuid().ToString("N"));
        var directory = Directory.CreateDirectory(Path.Combine(root, "qos" + qos)).FullName;
        var ticks = new TicksOptions { TickPeriod = TimeSpan.FromMilliseconds(.1),
            OutgoingWaitTicks = 1, ReceiveWaitPeriod = TimeSpan.FromSeconds(1), CounterHistoryDepth = 100 };
        var network = new NetworkOptions { NetworkType = "eth", TransferTicks = 1,
            CapacityBytesPerSecond = 100_000_000, ThroughputWindowTicks = 100, QueueCapacity = 100_000 };
        var options = new MqttRelayExperimentOptions
        {
            TopologyMode = ExperimentTopologyMode.BrokerFidelity,
            ProfileClientCountMode = ProfileClientCountMode.Publishers,
            Brokers = 1, NetworkLength = 1, ClientsPerBroker = 4,
            PublishQos = (MqttQos)qos, SubscribeQos = (MqttQos)qos,
            PayloadBytes = 16, BrokerAssignmentMode = MqttBrokerAssignmentMode.Ideal,
            WarmupTicks = 100, MeasurementTicks = 2000, DrainTimeoutTicks = 10000,
            OfferedRps = 10, RandomSeed = 42, LoadMode = PublishLoadMode.ClosedLoopPerPublisher,
            Parallel = false, RefreshStrategy = 0, EnableCounters = true
        };
        var runner = new MqttRelayExperimentRunner(ticks, network);
        var low = runner.Run(options, directory, new("technical-working-tree", "not-native", "ideal"));
        var ceiling = options.ClientsPerBroker / ticks.TickPeriod.TotalSeconds;
        options.OfferedRps = ceiling;
        var first = runner.Run(options, directory, new("technical-working-tree", "not-native", "ideal"));
        options.OfferedRps = ceiling * 2;
        var second = runner.Run(options, directory, new("technical-working-tree", "not-native", "ideal"));
        foreach (var result in new[] { low, first, second })
            Assert.AreEqual(ExperimentRunStatus.Completed, result.Summary.Status, result.Summary.Error);
        Assert.AreEqual(first.Summary.Simulated, second.Summary.Simulated,
            "Doubling a nonbinding token ceiling must not change scientific counters.");
        Assert.IsTrue(first.Summary.Simulated.Completed > low.Summary.Simulated.Completed,
            "The low-rate control must demonstrate that an ordinary OfferedRps can constrain the closed loop.");
        using var output = new FileStream(Path.Combine(directory, "verification.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(output, new { scope = "Ideal technical load-policy check; not native/model validation",
            qos, ceilingRps = ceiling, doubledCeilingRps = ceiling * 2,
            lowRate = low.Summary.Simulated, ceilingRate = first.Summary.Simulated,
            doubledCeilingRate = second.Summary.Simulated, equalScientificCounters = true,
            rawDirectories = new[] { low.RunDirectory, first.RunDirectory, second.RunDirectory } },
            new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(directory);
    }
}
