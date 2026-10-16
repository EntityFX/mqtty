using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.Tests.Integration;

[TestClass]
public class CompactCalibrationTests
{
    internal static JsonObject Observations(string broker = "Aedes")
    {
        var source = ScopedCalibrationTests.Observations(broker);
        source["runCount"] = 36;
        source["points"] = new JsonArray(source["points"]!.AsArray()
            .Where(point => point!["publishers"]!.GetValue<int>() is 1 or 16)
            .Select(point => JsonNode.Parse(point!.ToJsonString())).ToArray());
        return source;
    }

    private static byte[] Bytes(JsonObject source) => Encoding.UTF8.GetBytes(source.ToJsonString());
    private static byte[] Calibrate(JsonObject source, string broker = "Aedes") =>
        BrokerCalibrationV3.Calibrate(Bytes(source), new string('e', 40), broker: broker, coverageProtocol: "compact-v1");

    [TestMethod]
    public void Compact_RequiresExplicitCoverageAndBroker_AndDoesNotWeakenFullProtocol()
    {
        var source = Observations();
        Assert.ThrowsException<InvalidDataException>(() => BrokerCalibrationV3.Calibrate(Bytes(source), new string('e', 40), broker: "Aedes"));
        Assert.ThrowsException<InvalidDataException>(() => BrokerCalibrationV3.Calibrate(Bytes(source), new string('e', 40), coverageProtocol: "compact-v1"));
        Assert.ThrowsException<InvalidDataException>(() => BrokerCalibrationV3.Calibrate(Bytes(source), new string('e', 40), broker: "Aedes", coverageProtocol: "unknown"));
        var bytes = Calibrate(source);
        var artifact = JsonNode.Parse(bytes)!;
        Assert.AreEqual("broker-calibration-compact-v1", artifact["artifactType"]!.GetValue<string>());
        Assert.AreEqual("compact-v1", artifact["provenance"]!["coverageProtocol"]!.GetValue<string>());
        Assert.AreEqual(12, artifact["points"]!.AsArray().Count);
        var profiles = new BrokerBenchmarkRepository(Encoding.UTF8.GetString(bytes));
        Assert.AreEqual(100d, profiles.Get("Aedes").For(256, MqttQos.ExactlyOnce, 16).CapacityRps);
        artifact["artifactType"] = "broker-calibration";
        Assert.ThrowsException<InvalidDataException>(() => new BrokerBenchmarkRepository(artifact.ToJsonString()));
    }

    [DataTestMethod]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("repeat")]
    [DataRow("publishers")]
    [DataRow("pilot")]
    public void Compact_RejectsIncompleteOrMisdeclaredCoverage(string fault)
    {
        var source = Observations();
        if (fault == "missing") source["points"]!.AsArray().RemoveAt(0);
        if (fault == "duplicate") source["points"]![1] = JsonNode.Parse(source["points"]![0]!.ToJsonString());
        if (fault == "repeat") source["points"]![0]!["repeats"] = 2;
        if (fault == "publishers") source["points"]![0]!["publishers"] = 64;
        if (fault == "pilot") source["runCount"] = 3;
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(source));
    }

    [TestMethod]
    public void Compact_RejectsTamperedCapacityAndCoverageProvenance()
    {
        var artifact = JsonNode.Parse(Calibrate(Observations()))!;
        artifact["points"]![0]!["capacityRps"] = 999;
        Assert.ThrowsException<InvalidDataException>(() => new BrokerBenchmarkRepository(artifact.ToJsonString()));
        artifact = JsonNode.Parse(Calibrate(Observations()))!;
        artifact["provenance"]!["coverageProtocol"] = "full-v3";
        Assert.ThrowsException<InvalidDataException>(() => new BrokerBenchmarkRepository(artifact.ToJsonString()));
    }

    [TestMethod]
    public void Compact_IndependentReferenceIsExplicitAndNeverRecalibratesProfile()
    {
        var bytes = Calibrate(Observations());
        var before = (byte[])bytes.Clone();
        using var artifact = JsonDocument.Parse(bytes);
        var artifacts = new[] { new BrokerProfileArtifact("snapshot", new string('a', 64), artifact.RootElement.GetProperty("provenance").Clone()) };
        var control = Observations();
        control["provenance"]!["identity"]!["campaignId"] = "compact-control";
        Assert.ThrowsException<InvalidDataException>(() => IndependentObservationReference.EnsureIndependent(Bytes(control), artifacts));
        IndependentObservationReference.EnsureIndependent(Bytes(control), artifacts, "compact-v1");
        var options = new MqttRelayExperimentOptions { PayloadBytes = 256, PublishQos = MqttQos.AtLeastOnce, ClientsPerBroker = 16 };
        var reference = IndependentObservationReference.Read(Bytes(control), "Aedes", options, new string('e', 40), "compact-v1");
        Assert.AreEqual(80d, reference.CompletedRps);
        CollectionAssert.AreEqual(before, bytes);
        Assert.ThrowsException<InvalidDataException>(() => IndependentObservationReference.Read(Bytes(control), "Aedes", options, new string('e', 40)));
    }

    [TestMethod]
    public void ProfileSet_RejectsMixingCompactAndFullCoverage()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "compact-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var a = Path.Combine(root, "a.json");
            var b = Path.Combine(root, "b.json");
            File.WriteAllBytes(a, Calibrate(Observations()));
            File.WriteAllBytes(b, BrokerCalibrationV3.Calibrate(Bytes(ScopedCalibrationTests.Observations("Mosquitto")), new string('e', 40), broker: "Mosquitto"));
            Assert.ThrowsException<InvalidDataException>(() => new BrokerProfileSet(new[] { a, b }));
            File.WriteAllBytes(b, Calibrate(Observations("Mosquitto"), "Mosquitto"));
            Assert.AreEqual(2, new BrokerProfileSet(new[] { a, b }).Artifacts.Count);
        }
        finally { Directory.Delete(root, true); }
    }
}
