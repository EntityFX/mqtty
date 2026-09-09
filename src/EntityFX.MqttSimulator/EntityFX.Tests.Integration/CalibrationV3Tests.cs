using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Integration;

[TestClass]
public class CalibrationV3Tests
{
    // A complete, synthetic 96-point observation matrix; never presented as real measurements.
    internal static JsonObject Observations()
    {
        var points = new JsonArray();
        foreach (var broker in new[] { "Aedes", "Mosquitto", "EMQX", "ActiveMQ" })
        foreach (var bytes in new[] { 16, 256 })
        foreach (var qos in new[] { 0, 1, 2 })
        foreach (var publishers in new[] { 1, 16, 64, 128 })
        {
            JsonObject Stats(double mean) => new() { ["count"] = 3, ["mean"] = mean,
                ["standardDeviation"] = 0, ["ci95HalfWidth"] = 0, ["ci95Lower"] = mean,
                ["ci95Upper"] = mean, ["min"] = mean, ["max"] = mean };
            var quantiles = new JsonObject { ["minMs"] = 1, ["p50Ms"] = 3, ["p75Ms"] = 5,
                ["p95Ms"] = 7, ["p99Ms"] = 9, ["maxMs"] = 11 };
            var latencyStatistics = new JsonObject();
            foreach (var item in quantiles) latencyStatistics[item.Key] = Stats(item.Value!.GetValue<int>());
            points.Add(new JsonObject { ["broker"] = broker, ["messageBytes"] = bytes, ["qos"] = qos,
                ["publishers"] = publishers, ["repeats"] = 3, ["attemptedRps"] = Stats(100),
                ["completedRps"] = Stats(80), ["publishFailureRate"] = Stats(.2),
                ["deliveryLossRate"] = Stats(.1), ["observedLatencyQuantiles"] = qos == 0 ? null : quantiles,
                ["observedLatencyStatistics"] = qos == 0 ? null : latencyStatistics, ["rttBaselineMs"] = 2 });
        }
        return new JsonObject { ["schemaVersion"] = 3, ["runCount"] = 288,
            ["provenance"] = new JsonObject { ["identity"] = new JsonObject { ["campaignId"] = "synthetic-test",
                ["configSha256"] = new string('a', 64), ["standSha256"] = new string('b', 64),
                ["deploymentMode"] = "singleHostSequential", ["cpuMode"] = "singleCorePinned",
                ["mqttBenchmarkCommitSha"] = new string('c', 40) },
                ["inputSha256"] = new JsonObject { ["attempts/test/result.json"] = new string('d', 64) } },
            ["points"] = points };
    }

    internal static JsonObject Calibrate(JsonObject observations, double rate = 0)
    {
        var result = BrokerCalibrationV3.Calibrate(Encoding.UTF8.GetBytes(observations.ToJsonString()), new string('e', 40), rate);
        return JsonNode.Parse(result)!.AsObject();
    }

    [TestMethod]
    public void Calibration_RemovesRateFailuresAndOneOrTwoRtts_AndPreservesProvenance()
    {
        var input = Observations();
        var output = Calibrate(input, .1);
        var points = output["points"]!.AsArray();
        var qos1 = points.Single(x => x!["broker"]!.GetValue<string>() == "Aedes" &&
            x["messageBytes"]!.GetValue<int>() == 16 && x["qos"]!.GetValue<int>() == 1 && x["publishers"]!.GetValue<int>() == 1)!;
        Assert.AreEqual(100d, qos1["attemptedRps"]!.GetValue<double>());
        Assert.AreEqual(80d, qos1["targetCompletedRps"]!.GetValue<double>());
        Assert.AreEqual(90d, qos1["capacityRps"]!.GetValue<double>());
        Assert.AreEqual(1d / 9, qos1["conditionalPublishFailureRate"]!.GetValue<double>(), 1e-12);
        Assert.AreEqual(.1, qos1["conditionalDeliveryLossRate"]!.GetValue<double>());
        Assert.AreEqual(0d, qos1["processingLatencyQuantiles"]!["minMs"]!.GetValue<double>());
        Assert.AreEqual(7d, qos1["processingLatencyQuantiles"]!["p99Ms"]!.GetValue<double>());
        Assert.AreEqual(9d, qos1["observedLatencyQuantiles"]!["p99Ms"]!.GetValue<double>());
        var qos2 = points.First(x => x!["qos"]!.GetValue<int>() == 2)!;
        Assert.AreEqual(5d, qos2["processingLatencyQuantiles"]!["p99Ms"]!.GetValue<double>());
        var qos0 = points.First(x => x!["qos"]!.GetValue<int>() == 0)!;
        Assert.IsNull(qos0["processingLatencyQuantiles"]);
        Assert.AreEqual("notApplicable", qos0["latencyStatus"]!.GetValue<string>());
        Assert.AreEqual(input["provenance"]!.ToJsonString(), output["provenance"]!["observations"]!.ToJsonString());
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ToJsonString()))).ToLowerInvariant(),
            output["provenance"]!["observationsSha256"]!.GetValue<string>());
        var sample = new BrokerBenchmarkRepository(output.ToJsonString()).Get("Aedes").For(16, MqttQos.AtLeastOnce, 1);
        Assert.AreEqual(90d, sample.CapacityRps);
        Assert.AreEqual(1d / 9, sample.PublishFailureRate, 1e-12);
    }

    [TestMethod]
    public void Calibration_RejectsUncalibratableRateGate()
    {
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(Observations(), .21));
    }

    [TestMethod]
    public void Calibration_RejectsMissingProvenanceIncompleteCoverageAndInvalidLatency()
    {
        var input = Observations(); input["provenance"]!["identity"]!.AsObject().Remove("standSha256");
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(input));
        input = Observations(); input["points"]!.AsArray().RemoveAt(0);
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(input));
        input = Observations(); input["points"]![4]!["observedLatencyQuantiles"]!["p99Ms"] = -1;
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(input));
        input = Observations(); input["points"]![0]!.AsObject().Remove("attemptedRps");
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(input));
    }

    [TestMethod]
    public void Calibration_ReplaysExactlyAndReaderRejectsTamperedCalibration()
    {
        var result = Calibrate(Observations());
        Assert.AreEqual(result.ToJsonString(), Calibrate(Observations()).ToJsonString());
        result["points"]![0]!["conditionalPublishFailureRate"] = .9;
        Assert.ThrowsException<InvalidDataException>(() => new BrokerBenchmarkRepository(result.ToJsonString()));
    }

    [TestMethod]
    public void V3Sample_InterpolationPreservesObservedTargets()
    {
        var output = Calibrate(Observations());
        var sample = new BrokerBenchmarkRepository(output.ToJsonString()).Get("Aedes").For(16, MqttQos.AtLeastOnce, 8);
        Assert.AreEqual(80d, sample.TargetCompletedRps);
        Assert.AreEqual(100d, sample.AttemptedRps);
        Assert.AreEqual(.2, sample.TargetPublishFailureRate);
        Assert.AreEqual(9d, sample.ObservedLatencyQuantiles?.P99Ms);
        Assert.AreEqual(2d, sample.RttBaselineMs);
    }

    [TestMethod]
    public void Reader_RejectsMalformedJsonWithDomainError()
    {
        Assert.ThrowsException<InvalidDataException>(() => new BrokerBenchmarkRepository("{"));
    }

    [DataTestMethod]
    [DataRow("negativeRateMinimum")]
    [DataRow("rateMaximumAboveOne")]
    [DataRow("negativeLatencyMinimum")]
    [DataRow("wrongStudentTInterval")]
    public void Calibration_RejectsPhysicallyInvalidRepeatStatistics(string corruption)
    {
        var input = Observations();
        var point = input["points"]![4]!;
        switch (corruption)
        {
            case "negativeRateMinimum": point["publishFailureRate"]!["min"] = -1; break;
            case "rateMaximumAboveOne": point["deliveryLossRate"]!["max"] = 2; break;
            case "negativeLatencyMinimum": point["observedLatencyStatistics"]!["p99Ms"]!["min"] = -1; break;
            case "wrongStudentTInterval": point["attemptedRps"]!["standardDeviation"] = 10; break;
        }
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(input));
    }

    [DataTestMethod]
    [DataRow("missingHash")]
    [DataRow("wrongMode")]
    [DataRow("duplicateKey")]
    [DataRow("qos0Latency")]
    [DataRow("unknownField")]
    [DataRow("wrongCount")]
    public void Calibration_RejectsMalformedSchemaAndProvenance(string corruption)
    {
        var input = Observations();
        switch (corruption)
        {
            case "missingHash": input["provenance"]!["inputSha256"] = new JsonObject(); break;
            case "wrongMode": input["provenance"]!["identity"]!["cpuMode"] = "mixed"; break;
            case "duplicateKey": input["points"]![1]!["publishers"] = 1; break;
            case "qos0Latency": input["points"]![0]!["observedLatencyQuantiles"] = new JsonObject(); break;
            case "unknownField": input["unexpected"] = 1; break;
            case "wrongCount": input["points"]![0]!["attemptedRps"]!["count"] = 2; break;
        }
        Assert.ThrowsException<InvalidDataException>(() => Calibrate(input));
    }

    [TestMethod]
    public void Reader_RejectsWrongRootVersionTypesAndDuplicateProperties()
    {
        foreach (var input in new[] { "[]", "{\"schemaVersion\":\"3\"}", "{\"schemaVersion\":3,\"schemaVersion\":3}" })
            Assert.ThrowsException<InvalidDataException>(() => new BrokerBenchmarkRepository(input));
    }
}
