using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.Tests.Web;

/// <summary>Синтетические данные только для проверки исполнения, не натурные измерения.</summary>
internal static class TwinValidationFixture
{
    public static byte[] Profile()
    {
        JsonObject Stats(double mean) => new() { ["count"] = 3, ["mean"] = mean,
            ["standardDeviation"] = 0, ["ci95HalfWidth"] = 0, ["ci95Lower"] = mean,
            ["ci95Upper"] = mean, ["min"] = mean, ["max"] = mean };
        var points = new JsonArray();
        foreach (var size in new[] {16,256})
        foreach (var qos in new[] {0,1,2})
        foreach (var publishers in new[] {1,16})
        {
            var quantiles = new JsonObject { ["minMs"] = 1, ["p50Ms"] = 3, ["p75Ms"] = 5,
                ["p95Ms"] = 7, ["p99Ms"] = 9, ["maxMs"] = 11 };
            var latency = new JsonObject();
            foreach (var item in quantiles) latency[item.Key] = Stats(item.Value!.GetValue<int>());
            points.Add(new JsonObject { ["broker"] = "Mosquitto", ["messageBytes"] = size, ["qos"] = qos,
                ["publishers"] = publishers, ["repeats"] = 3, ["attemptedRps"] = Stats(100),
                ["completedRps"] = Stats(80), ["publishFailureRate"] = Stats(.2), ["deliveryLossRate"] = Stats(.1),
                ["observedLatencyQuantiles"] = qos == 0 ? null : quantiles,
                ["observedLatencyStatistics"] = qos == 0 ? null : latency, ["rttBaselineMs"] = 2 });
        }
        var observations = new JsonObject { ["schemaVersion"] = 3, ["runCount"] = 36,
            ["provenance"] = new JsonObject { ["identity"] = new JsonObject { ["campaignId"] = "synthetic-web-test",
                ["configSha256"] = new string('a',64), ["standSha256"] = new string('b',64),
                ["deploymentMode"] = "singleHostSequential", ["cpuMode"] = "singleCorePinned",
                ["mqttBenchmarkCommitSha"] = new string('c',40) },
                ["inputSha256"] = new JsonObject { ["attempts/test/result.json"] = new string('d',64) } }, ["points"] = points };
        var observationBytes=Encoding.UTF8.GetBytes(observations.ToJsonString());
        var histPoints=new JsonArray();var archive=1;
        foreach(var point in points.Where(p=>p!["qos"]!.GetValue<int>()!=0))
        {
            var repeats=new JsonArray();
            for(var repeat=1;repeat<=3;repeat++)
                repeats.Add(new JsonObject { ["repeat"]=repeat,["archiveSha256"]=archive++.ToString("x64"),
                    ["completedPublishes"]=1000,["actualMeasurementSeconds"]=12.5,
                    ["bins"]=JsonNode.Parse("[[1000,1],[3000,499],[5000,250],[7000,200],[9000,40],[11000,10]]") });
            histPoints.Add(new JsonObject { ["broker"]="Mosquitto",["messageBytes"]=point!["messageBytes"]!.GetValue<int>(),
                ["qos"]=point["qos"]!.GetValue<int>(),["publishers"]=point["publishers"]!.GetValue<int>(),["repeats"]=repeats });
        }
        var histogram=new JsonObject { ["schemaVersion"]=1,["artifactType"]="broker-publish-latency-histograms",
            ["observationsSha256"]=Hash(observationBytes),["campaignId"]="synthetic-web-test",["points"]=histPoints };
        return BrokerCalibrationEmpirical.Calibrate(observationBytes,Encoding.UTF8.GetBytes(histogram.ToJsonString()),
            new string('e',40),0,"Mosquitto","compact-v1");
    }
    public static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
