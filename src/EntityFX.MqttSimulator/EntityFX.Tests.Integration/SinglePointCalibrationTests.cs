using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.Tests.Integration;

/// <summary>Synthetic fixtures verify coverage and arithmetic, not broker accuracy.</summary>
[TestClass]
public class SinglePointCalibrationTests
{
    private const string Coverage = "single-point-v1";
    private static byte[] Bytes(JsonNode value) => Encoding.UTF8.GetBytes(value.ToJsonString());
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static JsonObject Observations(string broker = "Aedes")
    {
        var source = ScopedCalibrationTests.Observations(broker);
        source["runCount"] = 3;
        source["points"] = new JsonArray(source["points"]!.AsArray()
            .Where(p => p!["messageBytes"]!.GetValue<int>() == 16 && p["qos"]!.GetValue<int>() == 1
                && p["publishers"]!.GetValue<int>() == 16)
            .Select(p => JsonNode.Parse(p!.ToJsonString())).ToArray());
        return source;
    }

    private static JsonObject Histograms(JsonObject observations)
    {
        var repeats = new JsonArray();
        // Unequal sample counts: the merged CDF must be weighted by completions.
        for (var repeat = 1; repeat <= 3; repeat++)
            repeats.Add(new JsonObject { ["repeat"] = repeat, ["archiveSha256"] = repeat.ToString("x64"),
                ["completedPublishes"] = 1000 * repeat, ["actualMeasurementSeconds"] = 12.5 * repeat,
                ["bins"] = new JsonArray(new[] { (1000,1), (3000,499), (5000,250), (7000,200), (9000,40), (11000,10) }
                    .Select(b => (JsonNode)new JsonArray(b.Item1, b.Item2 * repeat)).ToArray()) });
        repeats[0]!["bins"]![0]![1] = 100;
        repeats[0]!["bins"]![1]![1] = 400;
        return new JsonObject { ["schemaVersion"] = 1, ["artifactType"] = "broker-publish-latency-histograms",
            ["observationsSha256"] = Hash(Bytes(observations)), ["campaignId"] = "synthetic-test",
            ["points"] = new JsonArray(new JsonObject { ["broker"] = observations["points"]![0]!["broker"]!.GetValue<string>(),
                ["messageBytes"] = 16, ["qos"] = 1, ["publishers"] = 16, ["repeats"] = repeats }) };
    }

    private static byte[] Calibrate(JsonObject source, string? broker = "Aedes", string? coverage = Coverage) =>
        BrokerCalibrationV3.Calibrate(Bytes(source), new string('e', 40), broker: broker, coverageProtocol: coverage);

    [DataTestMethod]
    [DataRow("Aedes")]
    [DataRow("Mosquitto")]
    [DataRow("ActiveMQ")]
    [DataRow("EMQX")]
    public void SinglePoint_RoundTripsOnlyExactCoverage(string broker)
    {
        var source = Observations(broker);
        var bytes = Calibrate(source, broker);
        var artifact = JsonNode.Parse(bytes)!;
        Assert.AreEqual("broker-calibration-single-point-v1", artifact["artifactType"]!.GetValue<string>());
        Assert.AreEqual(Coverage, artifact["provenance"]!["coverageProtocol"]!.GetValue<string>());
        Assert.AreEqual(3, artifact["runCount"]!.GetValue<int>());
        var profile = new BrokerBenchmarkRepository(Encoding.UTF8.GetString(bytes)).Get(broker);
        Assert.AreEqual(100d, profile.For(16, MqttQos.AtLeastOnce, 16).CapacityRps);
        Assert.ThrowsException<InvalidDataException>(() => profile.For(16, MqttQos.AtLeastOnce, 1));
        Assert.ThrowsException<InvalidDataException>(() => profile.For(16, MqttQos.AtLeastOnce, 64));
        Assert.ThrowsException<KeyNotFoundException>(() => profile.For(256, MqttQos.AtLeastOnce, 16));
        Assert.ThrowsException<KeyNotFoundException>(() => profile.For(16, MqttQos.ExactlyOnce, 16));
        Assert.ThrowsException<KeyNotFoundException>(() => profile.For(16, MqttQos.AtMostOnce, 16));
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(source, broker, null));
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(source, broker, "compact-v1"));
    }

    [DataTestMethod]
    [DataRow("missing")]
    [DataRow("extra")]
    [DataRow("repeat")]
    [DataRow("runCount")]
    [DataRow("broker")]
    [DataRow("size")]
    [DataRow("qos")]
    [DataRow("publishers")]
    [DataRow("scope")]
    [DataRow("protocol")]
    public void SinglePoint_RejectsMisdeclaredInputs(string fault)
    {
        var source = Observations();
        if (fault == "missing") source["points"]!.AsArray().Clear();
        if (fault == "extra") source["points"]!.AsArray().Add(JsonNode.Parse(source["points"]![0]!.ToJsonString()));
        if (fault == "repeat") source["points"]![0]!["repeats"] = 2;
        if (fault == "runCount") source["runCount"] = 36;
        if (fault == "broker") source["points"]![0]!["broker"] = "Mosquitto";
        if (fault == "size") source["points"]![0]!["messageBytes"] = 256;
        if (fault == "qos") source["points"]![0]!["qos"] = 2;
        if (fault == "publishers") source["points"]![0]!["publishers"] = 1;
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(source,
            fault == "scope" ? null : "Aedes", fault == "protocol" ? "single-point-v2" : Coverage));
    }

    [DataTestMethod]
    [DataRow("artifact")]
    [DataRow("protocol")]
    [DataRow("capacity")]
    [DataRow("point")]
    public void SinglePoint_ReaderRejectsRelabelledOrChangedProfile(string fault)
    {
        var artifact = JsonNode.Parse(Calibrate(Observations()))!;
        if (fault == "artifact") artifact["artifactType"] = "broker-calibration";
        if (fault == "protocol") artifact["provenance"]!["coverageProtocol"] = "compact-v1";
        if (fault == "capacity") artifact["points"]![0]!["capacityRps"] = 999;
        if (fault == "point") artifact["points"]![0]!["publishers"] = 64;
        Assert.ThrowsException<InvalidDataException>(() => new BrokerBenchmarkRepository(artifact.ToJsonString()));
    }

    [TestMethod]
    public void SinglePoint_EmpiricalProfileRetainsHashesAndWeightedSamples()
    {
        var source = Observations();
        var histograms = Histograms(source);
        var bytes = BrokerCalibrationEmpirical.Calibrate(Bytes(source), Bytes(histograms), new string('e', 40), 0, "Aedes", Coverage);
        var artifact = JsonNode.Parse(bytes)!;
        Assert.AreEqual(Hash(Bytes(source)), artifact["provenance"]!["observationsSha256"]!.GetValue<string>());
        Assert.AreEqual(Hash(Bytes(histograms)), artifact["provenance"]!["latencyHistogramsSha256"]!.GetValue<string>());
        var profile = new BrokerBenchmarkRepository(Encoding.UTF8.GetString(bytes)).Get("Aedes");
        var sample = profile.For(16, MqttQos.AtLeastOnce, 16);
        Assert.AreEqual(6000L, sample.ObservedLatencyDistribution!.Count);
        Assert.AreEqual(3d, sample.ObservedLatencyDistribution.Sample(.02),
            "Completion-weighted CDF gives 3 ms; equal weighting of the three CDFs would give 1 ms.");
        Assert.AreEqual(6000L, sample.ProcessingLatencyDistribution!.Count);
        Assert.ThrowsException<InvalidDataException>(() => profile.For(16, MqttQos.AtLeastOnce, 15));
    }

    [DataTestMethod]
    [DataRow("hash")]
    [DataRow("campaign")]
    [DataRow("point")]
    [DataRow("repeat")]
    [DataRow("count")]
    public void SinglePoint_EmpiricalRejectsUnrelatedOrIncompleteEvidence(string fault)
    {
        var source = Observations();
        var histograms = Histograms(source);
        if (fault == "hash") histograms["observationsSha256"] = new string('f', 64);
        if (fault == "campaign") histograms["campaignId"] = "control-must-not-train";
        if (fault == "point") histograms["points"]![0]!["publishers"] = 1;
        if (fault == "repeat") histograms["points"]![0]!["repeats"]![2]!["repeat"] = 2;
        if (fault == "count") histograms["points"]![0]!["repeats"]![0]!["completedPublishes"] = 999;
        Assert.ThrowsException<InvalidDataException>(() => BrokerCalibrationEmpirical.Calibrate(
            Bytes(source), Bytes(histograms), new string('e', 40), 0, "Aedes", Coverage));
    }

    [TestMethod]
    public void SinglePoint_IndependentControlRequiresExplicitMatchingCoverage()
    {
        var bytes = Calibrate(Observations());
        using var artifact = JsonDocument.Parse(bytes);
        var artifacts = new[] { new BrokerProfileArtifact("synthetic", Hash(bytes), artifact.RootElement.GetProperty("provenance").Clone()) };
        var control = Observations();
        Assert.ThrowsException<InvalidDataException>(() => IndependentObservationReference.EnsureIndependent(Bytes(control), artifacts, Coverage));
        control["provenance"]!["identity"]!["campaignId"] = "separate-control";
        Assert.ThrowsException<InvalidDataException>(() => IndependentObservationReference.EnsureIndependent(Bytes(control), artifacts));
        IndependentObservationReference.EnsureIndependent(Bytes(control), artifacts, Coverage);
        var reference = IndependentObservationReference.Read(Bytes(control), "Aedes", new MqttRelayExperimentOptions {
            PayloadBytes = 16, PublishQos = MqttQos.AtLeastOnce, ClientsPerBroker = 16 }, new string('e', 40), Coverage);
        Assert.AreEqual(80d, reference.CompletedRps);
        CollectionAssert.AreEqual(bytes, Calibrate(Observations()));
    }

    [TestMethod]
    public void SinglePoint_DoesNotRelaxLegacyProfileValidation()
    {
        var profile = new BrokerBenchmarkRepository(Encoding.UTF8.GetString(Calibrate(Observations()))).Get("Aedes");
        Assert.ThrowsException<InvalidDataException>(() => new MqttBrokerProfile {
            BrokerType = profile.BrokerType, MessageSizes = profile.MessageSizes }.Validate());
        Assert.ThrowsException<InvalidDataException>(() => new MqttBrokerProfile {
            BrokerType = profile.BrokerType, MessageSizes = profile.MessageSizes,
            CoverageProtocol = "unknown", RequireObservedPublisherCount = true }.Validate());
        Assert.ThrowsException<InvalidDataException>(() => new MqttBrokerProfile {
            BrokerType = profile.BrokerType, MessageSizes = profile.MessageSizes,
            CoverageProtocol = MqttBrokerProfile.SinglePointCoverage }.Validate());
    }

    [TestMethod]
    public void SinglePoint_CliReferenceAcceptsOnlyExplicitKnownCoverageWithAPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "single-point-cli-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllBytes(path, Calibrate(Observations()));
        try
        {
            var config = new EntityFX.MqttY.MqttRelay.App.MqttRelayExperimentRunConfiguration {
                MqttYRepository = "synthetic-repository", MqttBenchmarkRepository = "synthetic-benchmark",
                BrokerProfilePath = path, ReferenceObservationsPath = "control.json", ReferenceCoverageProtocol = Coverage,
                Experiment = new MqttRelayExperimentOptions { TopologyMode = ExperimentTopologyMode.BrokerFidelity,
                    Brokers = 1, NetworkLength = 1, ClientsPerBroker = 16, PayloadBytes = 16,
                    PublishQos = MqttQos.AtLeastOnce, SubscribeQos = MqttQos.AtLeastOnce,
                    MeasurementTicks = 100, DrainTimeoutTicks = 100, OfferedRps = 100 },
                Ticks = new TicksOptions { TickPeriod = TimeSpan.FromMilliseconds(.1), OutgoingWaitTicks = 1,
                    ReceiveWaitPeriod = TimeSpan.FromSeconds(1), CounterHistoryDepth = 100 },
                Network = new NetworkOptions { NetworkType = "eth", TransferTicks = 1,
                    CapacityBytesPerSecond = 100_000_000, QueueCapacity = 100_000, ThroughputWindowTicks = 100 } };
            config.Validate();
            config.ReferenceObservationsPath = null;
            Assert.ThrowsException<InvalidDataException>(config.Validate);
            config.ReferenceObservationsPath = "control.json";
            config.ReferenceCoverageProtocol = "unknown";
            Assert.ThrowsException<InvalidDataException>(config.Validate);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void SinglePoint_ProfileSetRejectsMixingCoverageButAcceptsOtherSinglePointBroker()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "single-point-set-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var a = Path.Combine(root, "a.json");
            var b = Path.Combine(root, "b.json");
            File.WriteAllBytes(a, Calibrate(Observations()));
            File.WriteAllBytes(b, BrokerCalibrationV3.Calibrate(Bytes(CompactCalibrationTests.Observations("Mosquitto")),
                new string('e', 40), broker: "Mosquitto", coverageProtocol: "compact-v1"));
            Assert.ThrowsException<InvalidDataException>(() => new BrokerProfileSet(new[] { a, b }));
            File.WriteAllBytes(b, Calibrate(Observations("Mosquitto"), "Mosquitto"));
            Assert.AreEqual(2, new BrokerProfileSet(new[] { a, b }).Artifacts.Count);
        }
        finally { Directory.Delete(root, true); }
    }
}
