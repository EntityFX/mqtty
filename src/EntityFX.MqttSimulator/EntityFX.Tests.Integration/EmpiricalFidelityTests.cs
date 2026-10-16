using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.Tests.Integration;

[TestClass]
public class EmpiricalFidelityTests
{
    private static byte[] Bytes(JsonNode value) => Encoding.UTF8.GetBytes(value.ToJsonString());
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static JsonObject Histograms(JsonObject observations)
    {
        var points = new JsonArray();
        var archive = 1;
        foreach (var p in observations["points"]!.AsArray().Where(p => p!["qos"]!.GetValue<int>() != 0))
        {
            var repeats = new JsonArray();
            for (var repeat = 1; repeat <= 3; repeat++)
                repeats.Add(new JsonObject { ["repeat"] = repeat, ["archiveSha256"] = archive++.ToString("x64"),
                    ["completedPublishes"] = 1000, ["actualMeasurementSeconds"] = 12.5,
                    ["bins"] = JsonNode.Parse("[[1000,1],[3000,499],[5000,250],[7000,200],[9000,40],[11000,10]]") });
            points.Add(new JsonObject { ["broker"] = p!["broker"]!.DeepCloneForTest(), ["messageBytes"] = p["messageBytes"]!.DeepCloneForTest(),
                ["qos"] = p["qos"]!.DeepCloneForTest(), ["publishers"] = p["publishers"]!.DeepCloneForTest(), ["repeats"] = repeats });
        }
        return new JsonObject { ["schemaVersion"] = 1, ["artifactType"] = "broker-publish-latency-histograms",
            ["observationsSha256"] = Hash(Bytes(observations)), ["campaignId"] = "synthetic-test", ["points"] = points };
    }
    private static byte[] Calibrate(JsonObject observations, JsonObject histogram) => BrokerCalibrationEmpirical.Calibrate(
        Bytes(observations), Bytes(histogram), new string('e', 40), 0, "Aedes", "compact-v1");

    [TestMethod]
    public void EmpiricalSampler_PreservesRareMaxWithoutInventingOnePercentTail()
    {
        var distribution = new EmpiricalLatencyDistribution(new[] { new LatencyBin(.2, 9999), new LatencyBin(200, 1) });
        Assert.AreEqual(.2, distribution.Sample(.999));
        Assert.AreEqual(200d, distribution.Sample(1));
        Assert.AreEqual(1, Enumerable.Range(0, 10000).Count(n => distribution.Sample((n + .5) / 10000) > 1));
        Assert.ThrowsException<InvalidDataException>(() => new EmpiricalLatencyDistribution(new[] { new LatencyBin(1, 1), new LatencyBin(1, 2) }));
        Assert.ThrowsException<InvalidDataException>(() => new EmpiricalLatencyDistribution(new[] { new LatencyBin(double.NaN, 1) }));
    }

    [TestMethod]
    public void EmpiricalProfile_RoundTripsAndRejectsUnobservedInterpolation()
    {
        var observations = CompactCalibrationTests.Observations();
        var bytes = Calibrate(observations, Histograms(observations));
        var profile = new BrokerBenchmarkRepository(Encoding.UTF8.GetString(bytes)).Get("Aedes");
        var sample = profile.For(16, MqttQos.AtLeastOnce, 1);
        Assert.AreEqual(3000L, sample.ProcessingLatencyDistribution!.Count);
        Assert.AreEqual(7d, sample.ProcessingLatencyDistribution.Sample(.989));
        Assert.ThrowsException<InvalidDataException>(() => profile.For(16, MqttQos.AtLeastOnce, 5));
        Assert.ThrowsException<InvalidDataException>(() => profile.For(16, MqttQos.AtLeastOnce, 128));
        Assert.IsNull(profile.For(16, MqttQos.AtMostOnce, 1).ProcessingLatencyDistribution);
        Assert.ThrowsException<InvalidDataException>(() => profile.For(16, MqttQos.AtMostOnce, 128));
    }

    [DataTestMethod]
    [DataRow("hash")]
    [DataRow("repeat")]
    [DataRow("count")]
    [DataRow("point")]
    [DataRow("p99")]
    public void EmpiricalProfile_RejectsMismatchedOrIncompleteEvidence(string fault)
    {
        var observations = CompactCalibrationTests.Observations();
        var histogram = Histograms(observations);
        if (fault == "hash") histogram["observationsSha256"] = new string('f', 64);
        if (fault == "repeat") histogram["points"]![0]!["repeats"]![1]!["repeat"] = 1;
        if (fault == "count") histogram["points"]![0]!["repeats"]![0]!["completedPublishes"] = 999;
        if (fault == "point") histogram["points"]!.AsArray().RemoveAt(0);
        if (fault == "p99") histogram["points"]![0]!["repeats"]![0]!["bins"]![4]![0] = 10000;
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(observations, histogram));
    }

    [TestMethod]
    public void EmpiricalProfile_RejectsChangedSnapshotProvenanceAndMixedLegacyProfiles()
    {
        var observations = CompactCalibrationTests.Observations();
        var bytes = Calibrate(observations, Histograms(observations));
        var changed = JsonNode.Parse(bytes)!;
        changed["histogramsBase64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
        Assert.ThrowsException<InvalidDataException>(() => new BrokerBenchmarkRepository(changed.ToJsonString()));
        changed = JsonNode.Parse(bytes)!;
        changed["provenance"]!["observationsSha256"] = new string('f', 64);
        Assert.ThrowsException<InvalidDataException>(() => new BrokerBenchmarkRepository(changed.ToJsonString()));
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "empirical-mixed-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var empirical = Path.Combine(root, "empirical.json");
            var legacy = Path.Combine(root, "legacy.json");
            File.WriteAllBytes(empirical, bytes);
            File.WriteAllBytes(legacy, BrokerCalibrationV3.Calibrate(Bytes(CompactCalibrationTests.Observations("Mosquitto")), new string('e', 40), broker: "Mosquitto", coverageProtocol: "compact-v1"));
            Assert.ThrowsException<InvalidDataException>(() => new BrokerProfileSet(new[] { empirical, legacy }));
            Assert.IsNotNull(new BrokerProfileSet(new[] { empirical }).Artifacts[0].Provenance);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Repository : IBrokerBenchmarkRepository
    {
        private readonly MqttBrokerProfile profile;
        public Repository(MqttBrokerProfile profile) { this.profile = profile; }
        public MqttBrokerProfile Get(string brokerType) => profile;
    }

    [TestMethod]
    public void ClosedLoop_DoesNotIssueMorePublishesWhileWaitingForAck()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "closed-loop-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var options = new MqttRelayExperimentOptions { TopologyMode = ExperimentTopologyMode.BrokerFidelity,
                ProfileClientCountMode = ProfileClientCountMode.Publishers, Brokers = 1, NetworkLength = 1, ClientsPerBroker = 1,
                PublishQos = MqttQos.AtLeastOnce, SubscribeQos = MqttQos.AtLeastOnce, PayloadBytes = 16,
                BrokerAssignmentMode = MqttBrokerAssignmentMode.Mosquitto, MeasurementTicks = 1000, DrainTimeoutTicks = 1000, OfferedRps = 1_000_000 };
            var ticks = new TicksOptions { TickPeriod = TimeSpan.FromMilliseconds(.1), OutgoingWaitTicks = 1, ReceiveWaitPeriod = TimeSpan.FromSeconds(1), CounterHistoryDepth = 100 };
            var network = new NetworkOptions { NetworkType = "eth", TransferTicks = 1, CapacityBytesPerSecond = 100_000_000, ThroughputWindowTicks = 100, QueueCapacity = 100000 };
            var runner = new MqttRelayExperimentRunner(ticks, network, new Repository(BrokerProfileTestData.Create("Mosquitto", 16, 2_000_000)));
            var open = runner.Run(options, root, new("y", "b", "p"));
            options.LoadMode = PublishLoadMode.ClosedLoopPerPublisher;
            var closed = runner.Run(options, root, new("y", "b", "p"));
            Assert.AreEqual(ExperimentRunStatus.Completed, closed.Summary.Status, closed.Summary.Error);
            Assert.IsTrue(closed.Summary.Simulated.Attempted < open.Summary.Simulated.Attempted / 5);
            Assert.AreEqual(closed.Summary.Simulated.Attempted, closed.Summary.Simulated.Completed);
        }
        finally { Directory.Delete(root, true); }
    }
}

internal static class JsonTestClone
{
    public static JsonNode DeepCloneForTest(this JsonNode value) => JsonNode.Parse(value.ToJsonString())!;
}
