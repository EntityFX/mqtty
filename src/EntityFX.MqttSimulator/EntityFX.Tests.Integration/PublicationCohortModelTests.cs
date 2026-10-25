using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.Tests.Integration;

[TestClass]
public class PublicationCohortModelTests
{
    [DataTestMethod]
    [DataRow(1, false)]
    [DataRow(2, false)]
    [DataRow(1, true)]
    [DataRow(2, true)]
    public void ModelTrace_RecordsActualOutcomes_AndSeparatesWarmupWithoutChangingMetrics(int qos, bool fixedHorizon)
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
            LoadMode = PublishLoadMode.ClosedLoopPerPublisher, RandomSeed = 42, ObserveFullDrainWindow = fixedHorizon };
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
        var connections = File.ReadAllLines(Path.Combine(observed.RunDirectory, "connections.ndjson"))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        var subscriber = connections.Where(row => row.GetProperty("role").GetString() == "subscriber").ToArray();
        Assert.IsTrue(subscriber.Any(row => row.GetProperty("state").GetString() == "connected"
            && row.GetProperty("phase").GetString() == "stateChange"), "A real state transition must be observed between phase snapshots.");
        Assert.IsTrue(subscriber.All(row => !string.IsNullOrWhiteSpace(row.GetProperty("clientId").GetString())));
        using var phaseEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(observed.RunDirectory, "phases.json")));
        Assert.AreEqual("everyModelTick", phaseEvidence.RootElement.GetProperty("connectionObservation").GetString());
        var subscriptionRows = File.ReadAllLines(Path.Combine(observed.RunDirectory, "subscriptions.ndjson"))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        Assert.IsTrue(subscriptionRows.Any(row => row.GetProperty("state").GetString() == "subscribed"
            && row.GetProperty("grantedQos").GetInt32() == qos
            && !string.IsNullOrWhiteSpace(row.GetProperty("topic").GetString())
            && row.GetProperty("elapsedSeconds").GetDouble() <= phaseEvidence.RootElement.GetProperty("measurementStartSeconds").GetDouble()));
        if (fixedHorizon)
            Assert.AreEqual(options.DrainTimeoutTicks * ticks.TickPeriod.TotalSeconds,
                phaseEvidence.RootElement.GetProperty("drainEndSeconds").GetDouble()
                - phaseEvidence.RootElement.GetProperty("measurementEndSeconds").GetDouble(), 1e-12);
    }
}
