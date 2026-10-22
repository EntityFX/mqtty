using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.LoadBalancing;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration;

[TestClass]
[TestCategory(TestCategories.Regression)]
[TestCategory("LoadBalancing")]
public class LoadBalancingTests
{
    [TestMethod]
    public void ConnectionPolicies_ReservePendingConnectionsAndKeepDeterministicMaps()
    {
        var roundRobin = ConnectionPolicies.Assign(BalancingMode.RoundRobin, 64, 4);
        var leastConnections = ConnectionPolicies.Assign(BalancingMode.LeastConnections, 64, 4);
        CollectionAssert.AreEqual(roundRobin, leastConnections,
            "Pending connections must reserve their backend before later CONNECT choices.");
        CollectionAssert.AreEqual(roundRobin, ConnectionPolicies.Assign(BalancingMode.DirectBalanced, 64, 4));
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 0, 1, 2 },
            ConnectionPolicies.Assign(BalancingMode.LeastConnections, 7, 4));
        foreach (var group in roundRobin.GroupBy(backend => backend)) Assert.AreEqual(16, group.Count());
        Assert.IsTrue(ConnectionPolicies.Assign(BalancingMode.Single, 64, 1).All(backend => backend == 0));
        var unchanged = roundRobin.ToArray();
        ConnectionPolicies.Assign(BalancingMode.RoundRobin, 3, 2);
        CollectionAssert.AreEqual(unchanged, roundRobin, "Assignments belong to a run, not shared mutable policy state.");
    }

    [TestMethod]
    public void ConnectionPolicies_RejectEmptyInputsAndUnknownMode()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => ConnectionPolicies.Assign(BalancingMode.RoundRobin, 0, 2));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => ConnectionPolicies.Assign(BalancingMode.RoundRobin, 2, 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => ConnectionPolicies.Assign((BalancingMode)999, 2, 2));
    }

    [TestMethod]
    public void ConnectionPolicies_RejectUnusedBackendsAndInvalidSinglePool()
    {
        foreach (var mode in Enum.GetValues<BalancingMode>())
            Assert.ThrowsException<ArgumentException>(() => ConnectionPolicies.Assign(mode, 1, 2));
        Assert.ThrowsException<ArgumentException>(() => ConnectionPolicies.Assign(BalancingMode.Single, 4, 2));
    }

    [TestMethod]
    public void DeliveryLedger_SeparatesWarmupWrongBackendUnknownAndDuplicates()
    {
        var ledger = new DeliveryLedger(16, .1);
        var warmup = ledger.Create(0, 0, false, 10);
        var measured = ledger.Create(0, 0, true, 20);
        ledger.Receive(0, warmup, 21);
        ledger.Receive(1, measured, 22);
        ledger.Receive(0, new byte[15], 23);
        ledger.Receive(0, new byte[16], 23);
        var corruptedTick = measured.ToArray();
        corruptedTick[8] ^= 1;
        ledger.Receive(0, corruptedTick, 24);
        ledger.Receive(0, measured, 25);
        ledger.Receive(0, measured, 30);

        var snapshot = ledger.Snapshot();
        Assert.AreEqual(1L, snapshot.MeasurementCreated);
        Assert.AreEqual(1L, snapshot.UniqueDelivered, "Wrong backend must not consume the first correct delivery.");
        Assert.AreEqual(1L, snapshot.DuplicateDeliveries);
        Assert.AreEqual(1L, snapshot.UnexpectedDeliveries);
        Assert.AreEqual(1L, snapshot.WarmupDeliveries);
        Assert.AreEqual(3L, snapshot.UnknownDeliveries);
        Assert.AreEqual(1L, snapshot.LatencyHistogramTicks[5]);
        Assert.AreEqual(1L, snapshot.Backends.Single(b => b.Backend == 1).UnexpectedDeliveries);
        Assert.AreEqual(snapshot.UniqueDelivered, snapshot.Backends.Sum(b => b.UniqueDelivered));
    }

    [TestMethod]
    public void DeliveryLedger_SnapshotCannotChangeWhenLaterMessagesArrive()
    {
        var ledger = new DeliveryLedger(256, .1);
        var first = ledger.Create(0, 0, true, 1);
        ledger.Receive(0, first, 6);
        var snapshot = ledger.Snapshot();
        var second = ledger.Create(1, 1, true, 7);
        ledger.Receive(1, second, 17);
        Assert.AreEqual(1L, snapshot.MeasurementCreated);
        Assert.AreEqual(1L, snapshot.UniqueDelivered);
        Assert.AreEqual(1, snapshot.Backends.Count);
        Assert.IsFalse(snapshot.LatencyHistogramTicks.ContainsKey(10));
        Assert.ThrowsException<NotSupportedException>(() =>
            ((IDictionary<long, long>)snapshot.LatencyHistogramTicks).Add(99, 99));
        Assert.ThrowsException<NotSupportedException>(() =>
            ((IDictionary<long, long>)snapshot.Backends[0].LatencyHistogramTicks).Add(99, 99));
        Assert.AreEqual(2L, ledger.Snapshot().UniqueDelivered);
    }

    [TestMethod]
    public void DeliveryLedger_RejectsImpossibleClockAndInvalidInputs()
    {
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new DeliveryLedger(15, .1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => new DeliveryLedger(16, double.NaN));
        var ledger = new DeliveryLedger(16, .1);
        var payload = ledger.Create(0, 0, true, 20);
        Assert.ThrowsException<InvalidDataException>(() => ledger.Receive(0, payload, 19));
        Assert.AreEqual(0L, ledger.Snapshot().UniqueDelivered);
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void IdealRun_DeliversMeasurementCohortAndFreezesSessionAssignments(int qos)
    {
        WithTemporaryRoot(root =>
        {
            var input = Prepare(Options() with { PublishQos = qos, SubscribeQos = qos });
            var assignments = input.Assignments.ToArray();
            var output = Path.Combine(root, "run");
            var summary = new LoadBalancingExperimentRunner().Run(input, output);
            Assert.AreEqual("completed", summary.Status, summary.Error);
            Assert.AreEqual(20L, summary.ScheduledPublishes);
            Assert.AreEqual(20L, summary.ApiAttempts);
            Assert.AreEqual(20L, summary.Delivery.MeasurementCreated);
            Assert.AreEqual(20L, summary.Delivery.UniqueDelivered);
            Assert.AreEqual(0L, summary.Delivery.DuplicateDeliveries);
            Assert.AreEqual(0L, summary.Delivery.UnexpectedDeliveries);
            Assert.AreEqual(0L, summary.Delivery.UnknownDeliveries);
            Assert.AreEqual(0L, summary.SendRejected);
            Assert.AreEqual(0, summary.PublisherPendingAfterDrain);
            Assert.AreEqual(0, summary.GraphErrors);
            CollectionAssert.AreEqual(assignments, input.Assignments, "PUBLISH must not rebalance an established connection.");
            Assert.AreEqual(20L, summary.Backends.Sum(b => b.Metrics.Attempted));
            Assert.AreEqual(20L, summary.Backends.Sum(b => b.Metrics.Completed));
            Assert.AreEqual(20L, summary.Backends.Sum(b => b.UniqueReceived));
            Assert.AreEqual(summary.Delivery.UniqueDelivered,
                summary.UniqueReceivedBeforeDrain + summary.UniqueReceivedDuringDrain);
            Assert.AreEqual(200L, summary.MeasurementEndTick - summary.MeasurementStartTick);
            Assert.AreEqual(1000d, summary.UniqueGoodput!.Value, 1e-9);
            if (qos == 0) Assert.IsNull(summary.PublishP99Ms);
            else Assert.IsTrue(summary.PublishP99Ms.HasValue && summary.PublishP99Ms > 0);
            Assert.IsTrue(summary.ReceiverP99Ms.HasValue && summary.ReceiverP99Ms > 0);
            Assert.AreEqual("completed", summary.Phases.Single(p => p.Phase == "drain").Status);

            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "sha256.json")));
            foreach (var item in document.RootElement.EnumerateObject())
            {
                var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, item.Name)))).ToLowerInvariant();
                Assert.AreEqual(item.Value.GetString(), hash, item.Name);
            }
            foreach (var artifact in new[] { "manifest.json", "summary.json", "report.md", "assignments.csv", "backend-metrics.csv", "histograms.json", "topology.graphml", "topology.puml" })
                Assert.IsTrue(File.Exists(Path.Combine(output, artifact)), artifact);
        });
    }

    [TestMethod]
    public void SameInputAndSeed_ReproduceScientificSummaryExactly()
    {
        WithTemporaryRoot(root =>
        {
            var input = Prepare(Options() with { PublishQos = 2, SubscribeQos = 2 });
            var runner = new LoadBalancingExperimentRunner();
            var first = runner.Run(input, Path.Combine(root, "first"));
            var second = runner.Run(input, Path.Combine(root, "second"));
            Assert.AreEqual("completed", first.Status, first.Error);
            Assert.AreEqual(JsonSerializer.Serialize(first, ExperimentInput.Json), JsonSerializer.Serialize(second, ExperimentInput.Json));
            Assert.AreEqual(File.ReadAllText(Path.Combine(root, "first", "summary.json")),
                File.ReadAllText(Path.Combine(root, "second", "summary.json")));
        });
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void EqualStaticAssignments_ProduceEqualDirectRoundRobinAndLeastConnectionsSummaries(int qos)
    {
        WithTemporaryRoot(root =>
        {
            string? expected = null;
            foreach (var mode in new[] { BalancingMode.DirectBalanced, BalancingMode.RoundRobin, BalancingMode.LeastConnections })
            {
                var input = Prepare(Options() with { Mode = mode, PublishQos = qos, SubscribeQos = qos });
                var summary = new LoadBalancingExperimentRunner().Run(input, Path.Combine(root, mode.ToString()));
                Assert.AreEqual("completed", summary.Status, summary.Error);
                var scientific = JsonSerializer.Serialize(summary, ExperimentInput.Json);
                if (expected == null) expected = scientific;
                else Assert.AreEqual(expected, scientific, "A zero-cost selection policy cannot invent gain for an identical connection map.");
            }
        });
    }

    [TestMethod]
    public void IndependentBackends_DoNotShareSubscriptions()
    {
        var topology = LoadBalancingTopologyBuilder.Build(Prepare(Options()));
        try
        {
            var graph = topology.Graph;
            var publisher = topology.Publishers[0];
            var local = topology.Receivers[0];
            var remote = topology.Receivers[1];
            publisher.BeginConnect(topology.Brokers[0].Name, true);
            local.BeginConnect(topology.Brokers[0].Name, true);
            remote.BeginConnect(topology.Brokers[1].Name, true);
            Until(() => publisher.IsConnected && local.IsConnected && remote.IsConnected);
            local.BeginSubscribe(LoadBalancingTopologyBuilder.Topic, MqttQos.AtLeastOnce);
            remote.BeginSubscribe(LoadBalancingTopologyBuilder.Topic, MqttQos.AtLeastOnce);
            Until(() => local.IsSubscribed(LoadBalancingTopologyBuilder.Topic) && remote.IsSubscribed(LoadBalancingTopologyBuilder.Topic));
            var localCount = 0;
            var remoteCount = 0;
            local.MessageReceived += (_, _) => localCount++;
            remote.MessageReceived += (_, _) => remoteCount++;
            Assert.IsTrue(publisher.Publish(LoadBalancingTopologyBuilder.Topic, new byte[16], MqttQos.AtLeastOnce));
            Until(() => graph.IsQuiescent);
            Assert.AreEqual(1, localCount, "The control receiver must establish that publication really occurred.");
            Assert.AreEqual(0, remoteCount, "A load-balancing selector does not create global subscriptions.");

            void Until(Func<bool> complete)
            {
                for (var i = 0; !complete() && i < 10000; i++)
                    Assert.IsTrue(graph.RefreshWithCounters(false, 0));
                Assert.IsTrue(complete(), "Connection, subscription, or drain barrier timed out.");
            }
        }
        finally { topology.Graph.Clear(); }
    }

    [TestMethod]
    public void Prepare_RequiresExactObservedPublisherCountsWithoutInterpolation()
    {
        var template = BrokerProfileTestData.Create("Aedes", 16, 1000, latencyMs: 1);
        var exact = new MqttBrokerProfile
        {
            BrokerType = template.BrokerType, MessageSizes = template.MessageSizes, RequireObservedPublisherCount = true
        };
        using var provenance = JsonDocument.Parse("{}");
        var snapshot = new ProfileSnapshot(new InputSnapshot("synthetic-profile", new byte[] { 1 }), exact, provenance.RootElement.Clone());
        var options = Options() with { Mode = BalancingMode.Single, BackendCount = 1, TotalPublishers = 1, Ideal = false };
        Assert.AreEqual(1, Prepare(options, snapshot).Assignments.Length);
        Assert.ThrowsException<InvalidDataException>(() => Prepare(options with { TotalPublishers = 2 }, snapshot));
    }

    [TestMethod]
    public void Configuration_RejectsUnrepresentableDurationsAndInvalidModes()
    {
        foreach (var options in new[]
        {
            Options() with { WarmupSeconds = -.01 },
            Options() with { MeasurementSeconds = 0 },
            Options() with { MeasurementSeconds = .00015 },
            Options() with { BackendCount = 0 },
            Options() with { Mode = BalancingMode.Single, BackendCount = 2 },
            Options() with { Ideal = false }
        }) Assert.ThrowsException<InvalidDataException>(() => Prepare(options));
        // JSON itself rejects NaN; exercise the programmatic configuration guard directly.
        Assert.ThrowsException<InvalidDataException>(() => (Options() with { DrainTimeoutSeconds = double.NaN }).Validate());
    }

    [TestMethod]
    public void ConfigurationFile_RejectsUnknownAndDuplicateFields()
    {
        WithTemporaryRoot(root =>
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "config.json");
            var json = JsonSerializer.Serialize(Options(), ExperimentInput.Json);
            var unknownTopLevel = JsonNode.Parse(json)!.AsObject();
            unknownTopLevel["offeredRpsTypo"] = 1000;
            File.WriteAllText(path, unknownTopLevel.ToJsonString());
            Assert.ThrowsException<InvalidDataException>(() => ExperimentInput.Load(path, false));
            var unknownNetworkField = JsonNode.Parse(json)!.AsObject();
            unknownNetworkField["network"]!["transferTickTypo"] = 2;
            File.WriteAllText(path, unknownNetworkField.ToJsonString());
            Assert.ThrowsException<InvalidDataException>(() => ExperimentInput.Load(path, false));
            File.WriteAllText(path, json.Insert(1, "\"ideal\":true,"));
            Assert.ThrowsException<InvalidDataException>(() => ExperimentInput.Load(path, false));
            File.WriteAllText(path, json);
            var input = ExperimentInput.Load(path, false).Single();
            Assert.AreEqual(4, input.Assignments.Length);
            CollectionAssert.AreEqual(File.ReadAllBytes(path), input.Configuration.Bytes);
        });
    }

    [DataTestMethod]
    [DataRow("unknown")]
    [DataRow("duplicate")]
    [DataRow("null")]
    public void Command_RejectsMalformedNestedProfileInputsBeforeCreatingOutput(string fault)
    {
        WithTemporaryRoot(root =>
        {
            Directory.CreateDirectory(root);
            var profile = fault switch
            {
                "unknown" => "{\"brokerType\":\"Aedes\",\"brokerProfilePath\":\"missing.json\",\"calibrationObservationsPath\":\"missing-observations.json\",\"unexpectedField\":true}",
                "duplicate" => "{\"brokerType\":\"Aedes\",\"brokerType\":\"Mosquitto\",\"brokerProfilePath\":\"missing.json\",\"calibrationObservationsPath\":\"missing-observations.json\"}",
                _ => "null"
            };
            var path = Path.Combine(root, "invalid-matrix.json");
            File.WriteAllText(path, "{\"ideal\":false,\"brokerProfiles\":[" + profile + "]}");
            if (fault != "null")
            {
                var error = Assert.ThrowsException<InvalidDataException>(() => ExperimentInput.Load(path, true));
                StringAssert.Contains(error.Message, "Unknown or duplicate configuration field",
                    "Reject the nested field itself, rather than merely rejecting the intentionally incomplete matrix.");
            }
            var output = Path.Combine(root, "should-not-exist");
            Assert.AreEqual(1, LoadBalancingCommand.Execute(new[] { "matrix", "--config", path, "--output", output }));
            Assert.IsFalse(Directory.Exists(output), "An invalid profile input must be rejected before output ownership is claimed.");
            Assert.AreEqual(1, Directory.GetFiles(root).Length);
        });
    }

    [TestMethod]
    public void Configuration_RejectsExcessiveLedgerAndTopologyBudgetsBeforeAllocation()
    {
        var boundary = Options() with { OfferedRps = 1000000, WarmupSeconds = 0, MeasurementSeconds = 1 };
        boundary.Validate();
        Assert.ThrowsException<InvalidDataException>(() => (boundary with { OfferedRps = 1000001 }).Validate());
        Assert.ThrowsException<InvalidDataException>(() => Prepare(Options() with
        {
            OfferedRps = 500001, WarmupSeconds = 1, MeasurementSeconds = 1
        }));
        Assert.ThrowsException<InvalidDataException>(() => Prepare(Options() with
        {
            TotalPublishers = 64, BackendCount = 64, NetworkLength = 100
        }));
    }

    [DataTestMethod]
    [DataRow("mqttYCommit")]
    [DataRow("benchmarkCommit")]
    [DataRow("cpuMode")]
    [DataRow("deploymentMode")]
    [DataRow("method")]
    public void Matrix_RejectsIncompatibleProfileProvenance(string difference)
    {
        WithTemporaryRoot(root =>
        {
            Directory.CreateDirectory(root);
            var profiles = new List<ProfileInput>();
            foreach (var broker in ExperimentInput.BrokerTypes)
            {
                var observations = ScopedCalibrationTests.Observations(broker);
                var changed = broker == "Mosquitto";
                var identity = observations["provenance"]!["identity"]!;
                if (changed && difference == "benchmarkCommit") identity["mqttBenchmarkCommitSha"] = new string('f', 40);
                if (changed && difference == "cpuMode") identity["cpuMode"] = "hostAllCores";
                if (changed && difference == "deploymentMode") identity["deploymentMode"] = "distributedHosts";
                var observationBytes = Encoding.UTF8.GetBytes(observations.ToJsonString());
                var profileBytes = BrokerCalibrationV3.Calibrate(observationBytes,
                    changed && difference == "mqttYCommit" ? new string('f', 40) : new string('e', 40), broker: broker);
                if (changed && difference == "method")
                {
                    var altered = JsonNode.Parse(profileBytes)!;
                    altered["provenance"]!["method"] = "unverified-other-method";
                    profileBytes = Encoding.UTF8.GetBytes(altered.ToJsonString());
                }
                File.WriteAllBytes(Path.Combine(root, broker + "-observations.json"), observationBytes);
                File.WriteAllBytes(Path.Combine(root, broker + "-profile.json"), profileBytes);
                profiles.Add(new(broker, broker + "-profile.json", broker + "-observations.json"));
            }
            var path = Path.Combine(root, "matrix.json");
            File.WriteAllText(path, JsonSerializer.Serialize(Options() with { Ideal = false, BrokerProfiles = profiles.ToArray() }, ExperimentInput.Json));
            Assert.ThrowsException<InvalidDataException>(() => ExperimentInput.Load(path, true));
        });
    }

    [TestMethod]
    public void Matrix_Loads144ComparableCasesAndRejectsObservationHashSubstitution()
    {
        WithTemporaryRoot(root =>
        {
            Directory.CreateDirectory(root);
            var profiles = new List<ProfileInput>();
            foreach (var broker in ExperimentInput.BrokerTypes)
            {
                var observations = Encoding.UTF8.GetBytes(ScopedCalibrationTests.Observations(broker).ToJsonString());
                var profile = BrokerCalibrationV3.Calibrate(observations, new string('e', 40), broker: broker);
                File.WriteAllBytes(Path.Combine(root, broker + "-observations.json"), observations);
                File.WriteAllBytes(Path.Combine(root, broker + "-profile.json"), profile);
                profiles.Add(new(broker, broker + "-profile.json", broker + "-observations.json"));
            }
            var options = Options() with { Ideal = false, BrokerProfiles = profiles.ToArray() };
            var path = Path.Combine(root, "matrix.json");
            File.WriteAllText(path, JsonSerializer.Serialize(options, ExperimentInput.Json));
            var matrix = ExperimentInput.Load(path, true);
            Assert.AreEqual(144, matrix.Count);
            foreach (var group in matrix.GroupBy(input => (input.Options.BrokerType, input.Options.PublishQos)))
            {
                Assert.AreEqual(12, group.Count());
                Assert.AreEqual(1, group.Select(input => input.Options.OfferedRps).Distinct().Count());
                foreach (var input in group)
                {
                    Assert.AreEqual(64, input.Options.TotalPublishers);
                    Assert.AreEqual(16, input.Options.PayloadBytes);
                    Assert.AreEqual(input.Options.Mode == BalancingMode.Single ? 1 : 4, input.Options.BackendCount);
                    Assert.IsNotNull(input.CalibrationObservations);
                    foreach (var backend in input.Assignments.GroupBy(value => value))
                        Assert.AreEqual(input.Options.Mode == BalancingMode.Single ? 64 : 16, backend.Count());
                }
            }
            File.AppendAllText(Path.Combine(root, "Aedes-observations.json"), "\n");
            Assert.ThrowsException<InvalidDataException>(() => ExperimentInput.Load(path, true),
                "Even semantically identical JSON must match the exact calibrated observation snapshot.");
        });
    }

    [TestMethod]
    public void StandaloneRunConfiguration_ReplaysWithoutOriginalConfigFile()
    {
        WithTemporaryRoot(root =>
        {
            var firstOutput = Path.Combine(root, "original");
            var first = new LoadBalancingExperimentRunner().Run(Prepare(Options()), firstOutput);
            var replay = ExperimentInput.Load(Path.Combine(firstOutput, "run-config.json"), false).Single();
            var second = new LoadBalancingExperimentRunner().Run(replay, Path.Combine(root, "replay"));
            Assert.AreEqual("completed", first.Status, first.Error);
            Assert.AreEqual(JsonSerializer.Serialize(first, ExperimentInput.Json), JsonSerializer.Serialize(second, ExperimentInput.Json));
        });
    }

    [TestMethod]
    public void ExistingOutput_IsNeverOverwritten()
    {
        WithTemporaryRoot(root =>
        {
            var output = Path.Combine(root, "existing");
            Directory.CreateDirectory(output);
            var evidence = Path.Combine(output, "original.txt");
            File.WriteAllText(evidence, "retain this evidence");
            Assert.ThrowsException<IOException>(() => new LoadBalancingExperimentRunner().Run(Prepare(Options()), output));
            Assert.AreEqual("retain this evidence", File.ReadAllText(evidence));
            CollectionAssert.AreEqual(new[] { evidence }, Directory.GetFiles(output));
        });
    }

    [TestMethod]
    public void RuntimeConnectTimeout_PreservesReasonAndEvidenceBundle()
    {
        WithTemporaryRoot(root =>
        {
            var output = Path.Combine(root, "failure");
            var summary = new LoadBalancingExperimentRunner().Run(Prepare(Options() with { DrainTimeoutSeconds = .0001 }), output);
            Assert.AreEqual("failed", summary.Status);
            StringAssert.Contains(summary.Error!, "connect timed out");
            Assert.AreEqual("failed", summary.Phases.Last().Status);
            Assert.IsNull(summary.BackendCompletedRps, "An incomplete measurement must not invent a throughput result.");
            foreach (var artifact in new[] { "summary.json", "manifest.json", "sha256.json", "report.md", "inputs/config.json" })
                Assert.IsTrue(File.Exists(Path.Combine(output, artifact)), artifact);
            StringAssert.Contains(File.ReadAllText(Path.Combine(output, "report.md")), "connect timed out");
        });
    }

    [TestMethod]
    public void CombinedP99_UsesMergedHistogramInsteadOfMeanBackendP99()
    {
        IReadOnlyDictionary<long, long> fast = new Dictionary<long, long> { [1] = 990 };
        IReadOnlyDictionary<long, long> slow = new Dictionary<long, long> { [100] = 10 };
        var merged = LoadBalancingExperimentRunner.MergeHistograms(new[] { fast, slow });
        Assert.AreEqual(.1, LoadBalancingExperimentRunner.Quantile(merged, .99, .1)!.Value, 1e-9);
        Assert.AreEqual(10d, LoadBalancingExperimentRunner.Quantile(merged, .999, .1)!.Value, 1e-9);
        Assert.AreEqual(1000L, merged.Values.Sum());
    }

    [TestMethod]
    public void RuntimeDrainTimeout_RetainsPartialMeasurementAndReportsIncompleteDelivery()
    {
        WithTemporaryRoot(root =>
        {
            var options = Options() with { PublishQos = 2, SubscribeQos = 2, WarmupSeconds = 0,
                MeasurementSeconds = .005, NetworkLength = 2, Network = new NetworkSettings { TransferTicks = 2 } };
            var runner = new LoadBalancingExperimentRunner();
            var control = runner.Run(Prepare(options), Path.Combine(root, "control"));
            Assert.AreEqual("completed", control.Status, control.Error);
            var barrierTicks = control.Phases.Where(p => p.Phase is "connect" or "subscribe")
                .Max(p => p.EndTick-p.StartTick);
            var failed = runner.Run(Prepare(options with { DrainTimeoutSeconds = barrierTicks*options.TickPeriod.TotalSeconds }), Path.Combine(root, "drain-failure"));
            Assert.AreEqual("failed", failed.Status);
            StringAssert.Contains(failed.Error!, "drain timed out");
            Assert.IsTrue(failed.ApiAttempts > 0);
            Assert.IsTrue(failed.MeasurementEndTick > failed.MeasurementStartTick);
            Assert.IsTrue(failed.PublisherPendingAfterDrain > 0);
            Assert.AreEqual("failed", failed.Phases.Last().Status);
            Assert.IsTrue(File.Exists(Path.Combine(root, "drain-failure", "sha256.json")));
        });
    }

    private static LoadBalancingOptions Options() => new()
    {
        Ideal = true, TotalPublishers = 4, BackendCount = 2, NetworkLength = 1,
        WarmupSeconds = .01, MeasurementSeconds = .02, DrainTimeoutSeconds = 1,
        TickPeriod = TimeSpan.FromTicks(1000), OfferedRps = 1000,
        Network = new NetworkSettings { TransferTicks = 1 }
    };

    private static PreparedExperiment Prepare(LoadBalancingOptions options, ProfileSnapshot? profile = null) =>
        ExperimentInput.Prepare(options,
            new InputSnapshot("synthetic", JsonSerializer.SerializeToUtf8Bytes(options, ExperimentInput.Json)), profile);

    private static void WithTemporaryRoot(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "mqtty-lb-test-" + Guid.NewGuid().ToString("N"));
        try { test(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
