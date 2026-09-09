using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;

namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

/// <summary>Explicit conversion of observations into admission and processing parameters.
/// Rate rejection is a model assumption, not a measurement inferred from successful throughput.</summary>
public static class BrokerCalibrationV3
{
    private static readonly string[] Knots = { "minMs", "p50Ms", "p75Ms", "p95Ms", "p99Ms", "maxMs" };
    private static readonly string[] Brokers = { "Aedes", "Mosquitto", "EMQX", "ActiveMQ" };
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static byte[] Calibrate(byte[] observationsBytes, string mqttYCommitSha, double rateRejectionRate = 0)
    {
        Hash(mqttYCommitSha, 40);
        Rate(rateRejectionRate);
        var source = Parse(observationsBytes);
        Fields(source, "schemaVersion", "runCount", "provenance", "points");
        Header(source);
        ValidateObservationProvenance(Object(source, "provenance"));
        var outputPoints = new JsonArray();
        foreach (var point in Points(source))
        {
            Fields(point, "broker", "messageBytes", "qos", "publishers", "repeats", "attemptedRps", "completedRps",
                "publishFailureRate", "deliveryLossRate", "observedLatencyQuantiles", "observedLatencyStatistics", "rttBaselineMs");
            var attempted = Statistics(Object(point, "attemptedRps"));
            var completed = Statistics(Object(point, "completedRps"));
            var failure = Statistics(Object(point, "publishFailureRate"), 1);
            var loss = Statistics(Object(point, "deliveryLossRate"), 1);
            ValidateTargets(attempted, completed, failure, loss);
            var qos = Integer(point, "qos");
            var rtt = Number(point, "rttBaselineMs");
            if (rtt < 0) throw Invalid("RTT must be non-negative.");
            var observed = Quantiles(point["observedLatencyQuantiles"], qos);
            if (qos == 0)
            {
                if (point["observedLatencyStatistics"] != null) throw Invalid("QoS0 latency statistics must be null.");
            }
            else
            {
                var statistics = Object(point, "observedLatencyStatistics");
                Fields(statistics, Knots);
                foreach (var knot in Knots)
                    Equal(Number(observed!, knot), Statistics(Object(statistics, knot)), "Latency statistics/quantiles differ.");
            }
            var conditional = ConditionalFailure(failure, rateRejectionRate);
            outputPoints.Add(new JsonObject
            {
                ["broker"] = Text(point, "broker"), ["messageBytes"] = Integer(point, "messageBytes"),
                ["qos"] = qos, ["publishers"] = Integer(point, "publishers"), ["repeats"] = 3,
                ["attemptedRps"] = attempted, ["targetCompletedRps"] = completed,
                ["targetPublishFailureRate"] = failure, ["rateRejectionRate"] = rateRejectionRate,
                ["capacityRps"] = attempted * (1 - rateRejectionRate),
                ["conditionalPublishFailureRate"] = conditional, ["conditionalDeliveryLossRate"] = loss,
                ["observedLatencyQuantiles"] = Clone(observed),
                ["processingLatencyQuantiles"] = Processing(observed, qos, rtt),
                ["latencyStatus"] = qos == 0 ? "notApplicable" : "observed",
                ["rttBaselineMs"] = rtt
            });
        }
        var output = new JsonObject
        {
            ["schemaVersion"] = 3, ["artifactType"] = "broker-calibration", ["runCount"] = 288,
            ["profileClientCountMode"] = "publishers",
            ["provenance"] = new JsonObject
            {
                ["observationsSha256"] = Convert.ToHexString(SHA256.HashData(observationsBytes)).ToLowerInvariant(),
                ["mqttYCommitSha"] = mqttYCommitSha,
                ["method"] = "conditional-failure-rtt-v1",
                ["observations"] = Clone(source["provenance"])
            },
            ["points"] = outputPoints
        };
        return JsonSerializer.SerializeToUtf8Bytes(output, Json);
    }

    internal static IReadOnlyDictionary<string, MqttBrokerProfile> ReadProfiles(byte[] bytes)
    {
        var source = Parse(bytes);
        Fields(source, "schemaVersion", "artifactType", "runCount", "profileClientCountMode", "provenance", "points");
        Header(source);
        if (Text(source, "artifactType") != "broker-calibration" || Text(source, "profileClientCountMode") != "publishers")
            throw Invalid("Expected broker-calibration artifact indexed by publishers.");
        var provenance = Object(source, "provenance");
        Fields(provenance, "observationsSha256", "mqttYCommitSha", "method", "observations");
        Hash(Text(provenance, "observationsSha256"), 64);
        Hash(Text(provenance, "mqttYCommitSha"), 40);
        if (Text(provenance, "method") != "conditional-failure-rtt-v1") throw Invalid("Unknown calibration method.");
        ValidateObservationProvenance(Object(provenance, "observations"));
        var profiles = new Dictionary<string, MqttBrokerProfile>(StringComparer.Ordinal);
        var points = Points(source).ToArray();
        foreach (var point in points)
        {
            Fields(point, "broker", "messageBytes", "qos", "publishers", "repeats", "attemptedRps", "targetCompletedRps",
                "targetPublishFailureRate", "rateRejectionRate", "capacityRps", "conditionalPublishFailureRate",
                "conditionalDeliveryLossRate", "observedLatencyQuantiles", "processingLatencyQuantiles", "latencyStatus", "rttBaselineMs");
            var attempted = Number(point, "attemptedRps");
            var completed = Number(point, "targetCompletedRps");
            var failure = Number(point, "targetPublishFailureRate");
            ValidateTargets(attempted, completed, failure, Number(point, "conditionalDeliveryLossRate"));
            var rate = Number(point, "rateRejectionRate");
            Equal(ConditionalFailure(failure, rate), Number(point, "conditionalPublishFailureRate"), "Conditional failure formula mismatch.");
            Equal(attempted * (1 - rate), Number(point, "capacityRps"), "Capacity/rate assumption mismatch.");
            var qos = Integer(point, "qos");
            var rtt = Number(point, "rttBaselineMs");
            if (rtt < 0) throw Invalid("RTT must be non-negative.");
            if (Text(point, "latencyStatus") != (qos == 0 ? "notApplicable" : "observed")) throw Invalid("Latency applicability mismatch.");
            var observed = Quantiles(point["observedLatencyQuantiles"], qos);
            var processing = Quantiles(point["processingLatencyQuantiles"], qos);
            var expected = Processing(observed, qos, rtt);
            if (qos != 0) foreach (var knot in Knots)
                Equal(Number(expected!, knot), Number(processing!, knot), "RTT subtraction mismatch.");
        }
        foreach (var broker in points.GroupBy(p => Text(p, "broker")))
        {
            var profile = new MqttBrokerProfile
            {
                BrokerType = broker.Key,
                MessageSizes = broker.GroupBy(p => Integer(p, "messageBytes")).ToDictionary(g => g.Key, g =>
                    new MqttMessageSizeProfile(g.Key, g.GroupBy(p => (MqttQos)Integer(p, "qos")).ToDictionary(q => q.Key, q =>
                        new CalibratedMqttQosProfile(q.OrderBy(p => Integer(p, "publishers")).Select(p =>
                            new CalibratedMqttQosSample(Integer(p, "publishers"), Number(p, "capacityRps"),
                                Number(p, "conditionalPublishFailureRate"), Number(p, "conditionalDeliveryLossRate"),
                                p["processingLatencyQuantiles"]?.Deserialize<LatencyQuantiles>(Json))
                            {
                                AttemptedRps = Number(p, "attemptedRps"), TargetCompletedRps = Number(p, "targetCompletedRps"),
                                TargetPublishFailureRate = Number(p, "targetPublishFailureRate"),
                                ObservedLatencyQuantiles = p["observedLatencyQuantiles"]?.Deserialize<LatencyQuantiles>(Json),
                                RttBaselineMs = Number(p, "rttBaselineMs")
                            }).ToArray()))))
            };
            profile.Validate();
            profiles.Add(broker.Key, profile);
        }
        return profiles;
    }

    private static double ConditionalFailure(double target, double rate)
    {
        Rate(target); Rate(rate);
        if (rate > target || rate == 1) throw Invalid("Uncalibratable point: rate rejection exceeds target failure or admits no messages.");
        return (target - rate) / (1 - rate);
    }

    private static void ValidateTargets(double attempted, double completed, double failure, double loss)
    {
        Rate(failure); Rate(loss);
        if (attempted <= 0 || completed < 0 || completed > attempted) throw Invalid("Invalid attempted/completed RPS.");
        // Rates and failure probabilities are means across repeats. Their ratio need not equal
        // the mean failure probability when repeat attempted rates differ.
    }

    private static JsonObject? Processing(JsonObject? observed, int qos, double rtt)
    {
        if (qos == 0) return null;
        var result = new JsonObject();
        foreach (var knot in Knots) result[knot] = Math.Max(0, Number(observed!, knot) - qos * rtt);
        return result;
    }

    private static JsonObject? Quantiles(JsonNode? value, int qos)
    {
        if (qos == 0)
        {
            if (value != null) throw Invalid("QoS0 latency must be null/notApplicable.");
            return null;
        }
        if (value is not JsonObject result) throw Invalid("QoS1/2 latency quantiles are required.");
        Fields(result, Knots);
        double previous = 0;
        foreach (var knot in Knots)
        {
            var number = Number(result, knot);
            if (number < previous) throw Invalid("Latency knots must be nonnegative and ordered.");
            previous = number;
        }
        return result;
    }

    private static IEnumerable<JsonObject> Points(JsonObject source)
    {
        if (source["points"] is not JsonArray points || points.Count != 96) throw Invalid("Requires all 96 points / 288 runs.");
        var keys = new HashSet<(string, int, int, int)>();
        foreach (var value in points)
        {
            if (value is not JsonObject point) throw Invalid("Point must be an object.");
            var key = (Text(point, "broker"), Integer(point, "messageBytes"), Integer(point, "qos"), Integer(point, "publishers"));
            if (!Brokers.Contains(key.Item1) || key.Item2 is not (16 or 256) || key.Item3 is < 0 or > 2 ||
                key.Item4 is not (1 or 16 or 64 or 128) || Integer(point, "repeats") != 3 || !keys.Add(key))
                throw Invalid("Unknown or duplicate point key, or wrong repeats.");
            yield return point;
        }
    }

    private static void Header(JsonObject source)
    {
        if (Integer(source, "schemaVersion") != 3 || Integer(source, "runCount") != 288)
            throw Invalid("Expected schemaVersion 3 and 288 successful runs.");
    }

    private static void ValidateObservationProvenance(JsonObject provenance)
    {
        Fields(provenance, "identity", "inputSha256");
        var identity = Object(provenance, "identity");
        Fields(identity, "campaignId", "configSha256", "standSha256", "deploymentMode", "cpuMode", "mqttBenchmarkCommitSha");
        Text(identity, "campaignId");
        Hash(Text(identity, "configSha256"), 64); Hash(Text(identity, "standSha256"), 64);
        Hash(Text(identity, "mqttBenchmarkCommitSha"), 40);
        if (Text(identity, "deploymentMode") is not ("singleHostSequential" or "distributedHosts") ||
            Text(identity, "cpuMode") is not ("singleCorePinned" or "hostAllCores")) throw Invalid("Unknown deployment/CPU mode.");
        var inputs = Object(provenance, "inputSha256");
        if (inputs.Count == 0) throw Invalid("Input hashes are required.");
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Key) || Path.IsPathRooted(input.Key) || input.Key.Split('/', '\\').Contains(".."))
                throw Invalid("Input hash paths must be relative.");
            Hash(Text(inputs, input.Key), 64);
        }
    }

    private static double Statistics(JsonObject value, double maximum = double.MaxValue)
    {
        Fields(value, "mean", "standardDeviation", "ci95HalfWidth", "ci95Lower", "ci95Upper", "min", "max", "count");
        var mean = Number(value, "mean");
        var half = Number(value, "ci95HalfWidth");
        var deviation = Number(value, "standardDeviation");
        var min = Number(value, "min");
        var max = Number(value, "max");
        // Averaging three binary64 values rounds the additions and division. Allow a
        // small multiple of machine epsilon at the data's scale, not an absolute
        // floor of 1 (which would hide large relative errors in very small rates).
        const double meanRelativeTolerance = 8 * 2.2204460492503131e-16;
        // Scale each comparison separately: a large maximum must not conceal a
        // mean materially below a tiny positive minimum.
        var lowerTolerance = Math.Max(double.Epsilon, meanRelativeTolerance * Math.Max(Math.Abs(mean), Math.Abs(min)));
        var upperTolerance = Math.Max(double.Epsilon, meanRelativeTolerance * Math.Max(Math.Abs(mean), Math.Abs(max)));
        if (Integer(value, "count") != 3 || deviation < 0 || half < 0 ||
            min < 0 || max > maximum || min > max || mean < 0 || mean > maximum ||
            min - mean > lowerTolerance || mean - max > upperTolerance)
            throw Invalid("Invalid statistics/count.");
        Equal(4.30265272975 * deviation / Math.Sqrt(3), half, "Invalid Student-t CI95 for three repeats.");
        Equal(mean - half, Number(value, "ci95Lower"), "Invalid CI lower bound.");
        Equal(mean + half, Number(value, "ci95Upper"), "Invalid CI upper bound.");
        return mean;
    }

    private static JsonObject Parse(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            ValidateUniqueProperties(document.RootElement);
            return JsonNode.Parse(bytes) as JsonObject ?? throw Invalid("Expected JSON object.");
        }
        catch (JsonException error) { throw new InvalidDataException("Invalid JSON.", error); }
    }

    private static void ValidateUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid("Duplicate JSON property.");
                ValidateUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateUniqueProperties(item);
    }

    private static void Fields(JsonObject value, params string[] expected)
    {
        if (value.Count != expected.Length || expected.Any(field => !value.ContainsKey(field)))
            throw Invalid("Missing or unknown schema field.");
    }
    private static JsonObject Object(JsonObject value, string name) => value[name] as JsonObject ?? throw Invalid($"Missing object {name}.");
    private static string Text(JsonObject value, string name) => value[name] is JsonValue item && item.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text) ? text : throw Invalid($"Missing string {name}.");
    private static double Number(JsonObject value, string name) => value[name] is JsonValue item && item.TryGetValue<double>(out var number) &&
        double.IsFinite(number) ? number : throw Invalid($"Missing finite number {name}.");
    private static int Integer(JsonObject value, string name) => value[name] is JsonValue item && item.TryGetValue<int>(out var number)
        ? number : throw Invalid($"Missing integer {name}.");
    private static void Rate(double rate) { if (!double.IsFinite(rate) || rate < 0 || rate > 1) throw Invalid("Rate must be in [0,1]."); }
    private static void Hash(string text, int length) { if (text.Length != length || text.Any(c => !Uri.IsHexDigit(c))) throw Invalid("Invalid provenance hash/commit."); }
    private static void Equal(double expected, double actual, string message) { if (Math.Abs(expected - actual) > 1e-10 * Math.Max(1, Math.Abs(expected))) throw Invalid(message); }
    private static JsonNode? Clone(JsonNode? value) => value == null ? null : JsonNode.Parse(value.ToJsonString());
    private static InvalidDataException Invalid(string message) => new(message);
}
