using System.Text;
using System.Text.Json.Nodes;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.Tests.Integration;

[TestClass]
public class ScopedCalibrationTests
{
    internal static JsonObject Observations(string broker)
    {
        var document = CalibrationV3Tests.Observations();
        document["runCount"] = 72;
        document["points"] = new JsonArray(document["points"]!.AsArray()
            .Where(point => point!["broker"]!.GetValue<string>() == broker)
            .Select(point => JsonNode.Parse(point!.ToJsonString())).ToArray());
        return document;
    }

    private static byte[] Calibrate(JsonObject observations, string broker) =>
        BrokerCalibrationV3.Calibrate(Encoding.UTF8.GetBytes(observations.ToJsonString()), new string('e', 40), broker: broker);

    [TestMethod]
    public void SingleBroker_RequiresExplicitScopeAndCompleteUniqueMatrix()
    {
        var observations = Observations("Aedes");
        var bytes = Calibrate(observations, "Aedes");
        var profile = new BrokerBenchmarkRepository(Encoding.UTF8.GetString(bytes));
        Assert.AreEqual(100d, profile.Get("Aedes").For(256, MqttQos.ExactlyOnce, 128).CapacityRps);
        Assert.ThrowsException<InvalidDataException>(() => CalibrationV3Tests.Calibrate(observations));
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(observations, "Mosquitto"));
        observations["points"]!.AsArray().RemoveAt(0);
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(observations, "Aedes"));
        observations = Observations("Aedes");
        observations["points"]![1]!["publishers"] = 1;
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(observations, "Aedes"));
        observations = Observations("Aedes");
        observations["points"]![0]!["repeats"] = 1;
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(observations, "Aedes"));
    }

    [TestMethod]
    public void ProfileSet_PreservesArtifactHashesAndRejectsDuplicateOrIncompatibleBrokers()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var a = Path.Combine(directory, "a.json");
            var b = Path.Combine(directory, "b.json");
            File.WriteAllBytes(a, Calibrate(Observations("Aedes"), "Aedes"));
            File.WriteAllBytes(b, Calibrate(Observations("Mosquitto"), "Mosquitto"));
            var set = new BrokerProfileSet(new[] { a, b });
            Assert.AreEqual(2, set.Artifacts.Count);
            Assert.IsNotNull(set.Artifacts[0].Provenance);
            Assert.AreEqual("Mosquitto", set.Get("Mosquitto").BrokerType);
            Assert.ThrowsException<InvalidDataException>(() => new BrokerProfileSet(new[] { a, a }));
            var other = Observations("Mosquitto");
            other["provenance"]!["identity"]!["cpuMode"] = "hostAllCores";
            File.WriteAllBytes(b, Calibrate(other, "Mosquitto"));
            Assert.ThrowsException<InvalidDataException>(() => new BrokerProfileSet(new[] { a, b }));
            var calibration = JsonNode.Parse(File.ReadAllBytes(a))!;
            calibration["points"]![0]!["capacityRps"] = 999;
            File.WriteAllText(a, calibration.ToJsonString());
            Assert.ThrowsException<InvalidDataException>(() => new BrokerProfileSet(new[] { a }));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void IndependentReference_SelectsNewObservationsWithoutChangingProfile()
    {
        var original = Calibrate(Observations("Aedes"), "Aedes");
        var source = Observations("Aedes");
        var changed = source["points"]!.AsArray().Single(point => point!["messageBytes"]!.GetValue<int>() == 16 &&
            point["qos"]!.GetValue<int>() == 1 && point["publishers"]!.GetValue<int>() == 1)!;
        foreach (var field in new[] { "mean", "min", "max", "ci95Lower", "ci95Upper" }) changed["completedRps"]![field] = 70;
        var options = new MqttRelayExperimentOptions { PayloadBytes = 16, PublishQos = MqttQos.AtLeastOnce, ClientsPerBroker = 1 };
        var reference = IndependentObservationReference.Read(Encoding.UTF8.GetBytes(source.ToJsonString()), "Aedes", options, new string('e', 40));
        Assert.AreEqual(70d, reference.CompletedRps);
        Assert.AreEqual(9d, reference.LatencyP99Ms);
        CollectionAssert.AreEqual(original, Calibrate(Observations("Aedes"), "Aedes"));
    }

    [TestMethod]
    public void IndependentReference_RejectsTrainingCampaignAndChangedStand()
    {
        var profile = JsonNode.Parse(Calibrate(Observations("Aedes"), "Aedes"))!;
        using var document = System.Text.Json.JsonDocument.Parse(profile["provenance"]!.ToJsonString());
        var artifacts = new[] { new BrokerProfileArtifact("test", new string('a', 64), document.RootElement.Clone()) };
        var source = Observations("Aedes");
        byte[] Bytes() => Encoding.UTF8.GetBytes(source.ToJsonString());
        Assert.ThrowsException<InvalidDataException>(() => IndependentObservationReference.EnsureIndependent(Bytes(), artifacts));
        source["provenance"]!["identity"]!["campaignId"] = "independent-test";
        IndependentObservationReference.EnsureIndependent(Bytes(), artifacts);
        source["provenance"]!["identity"]!["standSha256"] = new string('f', 64);
        Assert.ThrowsException<InvalidDataException>(() => IndependentObservationReference.EnsureIndependent(Bytes(), artifacts));
    }

    [TestMethod]
    public void ExperimentConfiguration_RejectsAmbiguousProfileSelection()
    {
        var config = new EntityFX.MqttY.MqttRelay.App.MqttRelayExperimentRunConfiguration();
        Assert.ThrowsException<InvalidDataException>(() => config.ProfilePaths());
        config.BrokerProfilePath = "one.json";
        CollectionAssert.AreEqual(new[] { "one.json" }, config.ProfilePaths());
        config.BrokerProfilePaths = new[] { "two.json" };
        Assert.ThrowsException<InvalidDataException>(() => config.ProfilePaths());
    }
}
