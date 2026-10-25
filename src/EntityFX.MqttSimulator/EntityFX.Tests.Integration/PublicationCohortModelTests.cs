using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.Tests.Integration;

[TestClass]
public class PublicationCohortModelTests
{
    [DataTestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void ModelTrace_RecordsActualOutcomes_AndSeparatesWarmupWithoutChangingMetrics(int qos)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cohort-model-" + Guid.NewGuid().ToString("N"))).FullName;
        var ticks = new TicksOptions { TickPeriod = TimeSpan.FromMilliseconds(.1), OutgoingWaitTicks = 1,
            ReceiveWaitPeriod = TimeSpan.FromSeconds(1), CounterHistoryDepth = 100 };
        var network = new NetworkOptions { NetworkType = "eth", TransferTicks = 1,
            CapacityBytesPerSecond = 100_000_000, QueueCapacity = 100_000, ThroughputWindowTicks = 100 };
        var options = new MqttRelayExperimentOptions { TopologyMode = ExperimentTopologyMode.BrokerFidelity,
            ProfileClientCountMode = ProfileClientCountMode.Publishers, Brokers = 1, NetworkLength = 1,
            ClientsPerBroker = 2, PayloadBytes = 16, PublishQos = (MqttQos)qos, SubscribeQos = (MqttQos)qos,
            WarmupTicks = 50, MeasurementTicks = 500, DrainTimeoutTicks = 2000, OfferedRps = 20000,
            LoadMode = PublishLoadMode.ClosedLoopPerPublisher, RandomSeed = 42 };
        var runner = new MqttRelayExperimentRunner(ticks, network);
        var original = runner.Run(options, root, new("working-tree", "not-native", "ideal"));
        options.RecordPublicationCohort = true;
        var observed = runner.Run(options, root, new("working-tree", "not-native", "ideal"));
        Assert.AreEqual(ExperimentRunStatus.Completed, observed.Summary.Status, observed.Summary.Error);
        Assert.AreEqual(original.Summary.Simulated, observed.Summary.Simulated, "Tracing must not alter scientific metrics.");
        var rows = File.ReadAllLines(Path.Combine(observed.RunDirectory, "publishes.ndjson"))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        var attempts = rows.Where(row => row.GetProperty("event").GetString() == "attempt").ToArray();
        Assert.AreEqual(attempts.Length, attempts.Select(row => row.GetProperty("id").GetString()).Distinct().Count());
        Assert.IsTrue(attempts.Any(row => row.GetProperty("id").GetString()!.StartsWith("00")));
        Assert.IsTrue(attempts.Any(row => row.GetProperty("id").GetString()!.StartsWith("01")));
        Assert.AreEqual(attempts.Length, rows.Count(row => row.GetProperty("event").GetString() == "completed"));
        Assert.IsTrue(attempts.All(row => row.GetProperty("payloadBytes").GetInt32() == 16));
        var delivered = File.ReadAllLines(Path.Combine(observed.RunDirectory, "deliveries.ndjson"))
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("id").GetString()).ToHashSet();
        Assert.IsTrue(attempts.All(row => delivered.Contains(row.GetProperty("id").GetString())));
        Assert.IsTrue(File.Exists(Path.Combine(observed.RunDirectory, "phases.json")));
    }
}
