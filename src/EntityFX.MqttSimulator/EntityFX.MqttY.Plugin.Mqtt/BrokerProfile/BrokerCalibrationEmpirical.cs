using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;

namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

/// <summary>Versioned empirical profile. The exact histogram snapshot and its raw archive provenance are retained.</summary>
public static class BrokerCalibrationEmpirical
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static byte[] Calibrate(byte[] observations, byte[] histograms, string commit,
        double rateRejectionRate, string broker, string? coverage)
    {
        var calibration = JsonNode.Parse(BrokerCalibrationV3.Calibrate(observations, commit,
            rateRejectionRate, broker, coverage))!;
        var hash = Hash(histograms);
        var provenance = JsonNode.Parse(calibration["provenance"]!.ToJsonString())!;
        provenance["method"] = "empirical-histogram-rtt-v1";
        provenance["latencyHistogramsSha256"] = hash;
        var result = new JsonObject
        {
            ["schemaVersion"] = 4, ["artifactType"] = "broker-calibration-empirical-v1",
            ["calibration"] = calibration, ["histogramsBase64"] = Convert.ToBase64String(histograms),
            ["provenance"] = provenance
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, Json);
        _ = ReadProfiles(bytes);
        return bytes;
    }

    internal static IReadOnlyDictionary<string, MqttBrokerProfile> ReadProfiles(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Fields(root, "schemaVersion", "artifactType", "calibration", "histogramsBase64", "provenance");
        if (root.GetProperty("schemaVersion").GetInt32() != 4 || root.GetProperty("artifactType").GetString() != "broker-calibration-empirical-v1")
            throw new InvalidDataException("Unsupported empirical profile.");
        var calibration = root.GetProperty("calibration");
        var profiles = BrokerCalibrationV3.ReadProfiles(Encoding.UTF8.GetBytes(calibration.GetRawText()));
        if (profiles.Count != 1) throw new InvalidDataException("Empirical profiles must have one broker.");
        byte[] histogramBytes;
        try { histogramBytes = Convert.FromBase64String(root.GetProperty("histogramsBase64").GetString()!); }
        catch (FormatException error) { throw new InvalidDataException("Invalid histogram snapshot.", error); }
        var expected = JsonNode.Parse(calibration.GetProperty("provenance").GetRawText())!;
        expected["method"] = "empirical-histogram-rtt-v1";
        expected["latencyHistogramsSha256"] = Hash(histogramBytes);
        // Compare structure, not input property ordering.
        var actual = root.GetProperty("provenance");
        Fields(actual, expected.AsObject().Select(pair => pair.Key).ToArray());
        foreach (var pair in expected.AsObject())
        {
            using var expectedDocument = JsonDocument.Parse(pair.Value!.ToJsonString());
            if (Canonical(actual.GetProperty(pair.Key)) != Canonical(expectedDocument.RootElement))
                throw new InvalidDataException("Empirical provenance mismatch.");
        }
        using var histogramDocument = JsonDocument.Parse(histogramBytes);
        var source = histogramDocument.RootElement;
        Fields(source, "schemaVersion", "artifactType", "observationsSha256", "campaignId", "points");
        if (source.GetProperty("schemaVersion").GetInt32() != 1 || source.GetProperty("artifactType").GetString() != "broker-publish-latency-histograms" ||
            source.GetProperty("observationsSha256").GetString() != calibration.GetProperty("provenance").GetProperty("observationsSha256").GetString() ||
            source.GetProperty("campaignId").GetString() != calibration.GetProperty("provenance").GetProperty("observations").GetProperty("identity").GetProperty("campaignId").GetString())
            throw new InvalidDataException("Histogram source differs from calibration observations.");
        var nativePoints = calibration.GetProperty("points").EnumerateArray().Where(p => p.GetProperty("qos").GetInt32() != 0).ToArray();
        var supplied = source.GetProperty("points").EnumerateArray().ToArray();
        if (supplied.Length != nativePoints.Length) throw new InvalidDataException("Incomplete empirical point matrix.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var archives = new HashSet<string>(StringComparer.Ordinal);
        foreach (var point in supplied)
        {
            Fields(point, "broker", "messageBytes", "qos", "publishers", "repeats");
            var name = point.GetProperty("broker").GetString()!;
            var size = point.GetProperty("messageBytes").GetInt32();
            var qos = point.GetProperty("qos").GetInt32();
            var clients = point.GetProperty("publishers").GetInt32();
            var key = $"{name}/{size}/{qos}/{clients}";
            if (!seen.Add(key) || qos is not (1 or 2) || !profiles.ContainsKey(name)) throw new InvalidDataException("Duplicate or unknown empirical point.");
            var native = nativePoints.SingleOrDefault(p => p.GetProperty("broker").GetString() == name &&
                p.GetProperty("messageBytes").GetInt32() == size && p.GetProperty("qos").GetInt32() == qos && p.GetProperty("publishers").GetInt32() == clients);
            if (native.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("Unexpected empirical point.");
            var repeats = point.GetProperty("repeats").EnumerateArray().ToArray();
            if (repeats.Length != 3 || !repeats.Select(r => r.GetProperty("repeat").GetInt32()).OrderBy(n => n).SequenceEqual(new[] { 1, 2, 3 }))
                throw new InvalidDataException("Empirical point needs unique repeats 1/2/3.");
            var bins = new SortedDictionary<long, long>();
            double meanRps = 0;
            var quantileNames = new[] { "minMs", "p50Ms", "p75Ms", "p95Ms", "p99Ms", "maxMs" };
            var probabilities = new[] { 0d, .5, .75, .95, .99, 1d };
            var meanQuantiles = new double[probabilities.Length];
            foreach (var repeat in repeats)
            {
                Fields(repeat, "repeat", "archiveSha256", "completedPublishes", "actualMeasurementSeconds", "bins");
                var archive = repeat.GetProperty("archiveSha256").GetString()!;
                if (archive.Length != 64 || archive.Any(c => !Uri.IsHexDigit(c)) || !archives.Add(archive)) throw new InvalidDataException("Invalid or reused raw archive hash.");
                var samples = repeat.GetProperty("completedPublishes").GetInt64();
                var seconds = repeat.GetProperty("actualMeasurementSeconds").GetDouble();
                if (samples <= 0 || !double.IsFinite(seconds) || seconds <= 0) throw new InvalidDataException("Invalid empirical sample count/duration.");
                var values = new List<LatencyBin>();
                foreach (var bin in repeat.GetProperty("bins").EnumerateArray())
                {
                    if (bin.GetArrayLength() != 2) throw new InvalidDataException("Expected microseconds/count pairs.");
                    var micros = bin[0].GetInt64(); var count = bin[1].GetInt64();
                    if (micros < 0 || micros > 86_400_000_000L) throw new InvalidDataException("Empirical latency outside supported range.");
                    values.Add(new LatencyBin(micros / 1000.0, count));
                    bins[micros] = checked(bins.GetValueOrDefault(micros) + count);
                }
                var distribution = new EmpiricalLatencyDistribution(values);
                if (distribution.Count != samples) throw new InvalidDataException("Empirical count differs from raw completions.");
                meanRps += samples / seconds / 3;
                for (var q = 0; q < probabilities.Length; q++)
                {
                    var rank = Math.Max(1, Math.Ceiling(probabilities[q] * samples));
                    meanQuantiles[q] += distribution.Sample((rank - .5) / samples) / 3;
                }
            }
            var targetRps = native.GetProperty("targetCompletedRps").GetDouble();
            if (Math.Abs(meanRps - targetRps) > Math.Max(1e-8, targetRps * 1e-10) ||
                meanQuantiles.Where((value, index) => Math.Abs(value - native.GetProperty("observedLatencyQuantiles").GetProperty(quantileNames[index]).GetDouble()) > .00101).Any())
                throw new InvalidDataException("Empirical statistics differ from observation targets.");
            var offset = qos * native.GetProperty("rttBaselineMs").GetDouble();
            var processing = bins.Select(pair => new LatencyBin(Math.Max(0, pair.Key / 1000.0 - offset), pair.Value))
                .GroupBy(bin => bin.Milliseconds).Select(group => new LatencyBin(group.Key, group.Sum(bin => bin.Count)));
            var profile = profiles[name].MessageSizes[size].Qos[(MqttQos)qos];
            profiles[name].MessageSizes[size].Qos[(MqttQos)qos] = new CalibratedMqttQosProfile(profile.Samples.Select(sample =>
                sample.Clients == clients ? sample with {
                    ProcessingLatencyDistribution = new EmpiricalLatencyDistribution(processing),
                    ObservedLatencyDistribution = new EmpiricalLatencyDistribution(bins.Select(pair => new LatencyBin(pair.Key / 1000.0, pair.Value)))
                } : sample).ToArray());
        }
        return profiles.ToDictionary(pair => pair.Key, pair => new MqttBrokerProfile {
            BrokerType = pair.Value.BrokerType, MessageSizes = pair.Value.MessageSizes, RequireObservedPublisherCount = true });
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonical(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        _ => value.GetRawText()
    };
    private static void Fields(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(names.OrderBy(n => n, StringComparer.Ordinal)))
            throw new InvalidDataException("Unexpected empirical artifact fields.");
    }
}
