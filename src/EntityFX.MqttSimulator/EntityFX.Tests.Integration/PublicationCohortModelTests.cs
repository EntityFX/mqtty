using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;

namespace EntityFX.Tests.Integration;

[TestClass]
public class PublicationCohortModelTests
{
    [TestMethod]
    public void ModelTrace_RecordsSubscriberDisconnectAndReconnectThroughActualClientLifecycle()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cohort-reconnect-" + Guid.NewGuid().ToString("N"))).FullName;
        var ticks = new TicksOptions { TickPeriod = TimeSpan.FromMilliseconds(.1), OutgoingWaitTicks = 1,
            ReceiveWaitPeriod = TimeSpan.FromSeconds(1), CounterHistoryDepth = 100 };
        var network = new NetworkOptions { NetworkType = "eth", TransferTicks = 1,
            CapacityBytesPerSecond = 100_000_000, QueueCapacity = 100_000, ThroughputWindowTicks = 100 };
        var options = new MqttRelayExperimentOptions { TopologyMode = ExperimentTopologyMode.BrokerFidelity,
            ProfileClientCountMode = ProfileClientCountMode.Publishers, Brokers = 1, NetworkLength = 1,
            ClientsPerBroker = 2, PayloadBytes = 16, PublishQos = MqttQos.AtLeastOnce, SubscribeQos = MqttQos.AtLeastOnce,
            WarmupTicks = 50, MeasurementTicks = 500, DrainTimeoutTicks = 2000, OfferedRps = 20000,
            LoadMode = PublishLoadMode.ClosedLoopPerPublisher, RandomSeed = 42,
            ObserveFullDrainWindow = true, RecordPublicationCohort = true };
        MqttClient? subscriber = null;
        string? server = null;
        string? clientId = null;
        var disconnected = false;
        var reconnectStarted = false;
        var result = new MqttRelayExperimentRunner(ticks, network).Run(options, root,
            new("working-tree", "not-native", "ideal"), onProgress: (graph, phase) =>
            {
                if (phase == "subscribe")
                {
                    var receiver = graph.Applications.Values.OfType<MqttReceiver>().Single();
                    subscriber = graph.Clients.Values.OfType<MqttClient>()
                        .Single(client => client.Name == receiver.Name + receiver.Options!.Server);
                    server = subscriber.Server;
                    clientId = subscriber.ClientId;
                    Assert.IsTrue(subscriber.IsConnected);
                    disconnected = subscriber.Disconnect();
                    Assert.IsFalse(subscriber.IsConnected);
                }
                if (phase == "measure")
                {
                    Assert.IsNotNull(subscriber);
                    reconnectStarted = subscriber.BeginConnect(server!, false);
                }
            });
        Assert.AreEqual(ExperimentRunStatus.Completed, result.Summary.Status, result.Summary.Error);
        Assert.IsTrue(disconnected, "The real model client must detach from its server and network.");
        Assert.IsTrue(reconnectStarted, "Reconnect must enqueue a real MQTT CONNECT exchange.");
        var rows = File.ReadAllLines(Path.Combine(result.RunDirectory, "connections.ndjson"))
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .Where(row => row.GetProperty("role").GetString() == "subscriber").ToArray();
        Assert.IsTrue(rows.Length > 0);
        Assert.IsTrue(rows.All(row => row.GetProperty("clientId").GetString() == clientId));
        var transitions = rows.Where(row => row.GetProperty("phase").GetString() == "stateChange").ToArray();
        var interruption = transitions.Single(row => row.GetProperty("state").GetString() == "disconnected");
        var recovery = transitions.Last(row => row.GetProperty("state").GetString() == "connected");
        Assert.IsTrue(recovery.GetProperty("elapsedSeconds").GetDouble()
            > interruption.GetProperty("elapsedSeconds").GetDouble());
        Assert.AreEqual("connected", rows.Last().GetProperty("state").GetString());
    }

    [DataTestMethod]
    [DataRow(1, false, 1)]
    [DataRow(2, false, 2)]
    [DataRow(1, true, 1)]
    [DataRow(2, true, 2)]
    [DataRow(2, true, 1)]
    public void ModelTrace_RecordsActualOutcomes_AndSeparatesWarmupWithoutChangingMetrics(int qos, bool fixedHorizon, int subscribeQos)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "cohort-model-" + Guid.NewGuid().ToString("N"))).FullName;
        var ticks = new TicksOptions { TickPeriod = TimeSpan.FromMilliseconds(.1), OutgoingWaitTicks = 1,
            ReceiveWaitPeriod = TimeSpan.FromSeconds(1), CounterHistoryDepth = 100 };
        var network = new NetworkOptions { NetworkType = "eth", TransferTicks = 1,
            CapacityBytesPerSecond = 100_000_000, QueueCapacity = 100_000, ThroughputWindowTicks = 100 };
        var options = new MqttRelayExperimentOptions { TopologyMode = ExperimentTopologyMode.BrokerFidelity,
            ProfileClientCountMode = ProfileClientCountMode.Publishers, Brokers = 1, NetworkLength = 1,
            ClientsPerBroker = 2, PayloadBytes = 16, PublishQos = (MqttQos)qos, SubscribeQos = (MqttQos)subscribeQos,
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
            && row.GetProperty("grantedQos").GetInt32() == subscribeQos
            && row.GetProperty("requestedQos").GetInt32() == subscribeQos
            && !string.IsNullOrWhiteSpace(row.GetProperty("topic").GetString())
            && row.GetProperty("elapsedSeconds").GetDouble() <= phaseEvidence.RootElement.GetProperty("measurementStartSeconds").GetDouble()));
        if (fixedHorizon)
            Assert.AreEqual(options.DrainTimeoutTicks * ticks.TickPeriod.TotalSeconds,
                phaseEvidence.RootElement.GetProperty("drainEndSeconds").GetDouble()
                - phaseEvidence.RootElement.GetProperty("measurementEndSeconds").GetDouble(), 1e-12);
    }
}
