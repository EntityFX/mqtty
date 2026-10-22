using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntityFX.MqttY.LoadBalancing;

public static class ExperimentInput
{
    public static readonly string[] BrokerTypes = { "Aedes", "Mosquitto", "ActiveMQ", "EMQX" };
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = false, WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static IReadOnlyList<PreparedExperiment> Load(string path, bool matrix)
    {
        var source = InputSnapshot.Read(path);
        // Reject unknown/duplicate fields rather than silently accepting misspelled experiment inputs.
        using var document = JsonDocument.Parse(source.Bytes);
        var allowed = typeof(LoadBalancingOptions).GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToHashSet();
        CheckProperties(document.RootElement, allowed);
        if (document.RootElement.TryGetProperty("network", out var network))
            CheckProperties(network, typeof(NetworkSettings).GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToHashSet());
        if (document.RootElement.TryGetProperty("brokerProfiles", out var profiles) && profiles.ValueKind != JsonValueKind.Null)
            foreach (var profile in profiles.EnumerateArray())
                CheckProperties(profile, new HashSet<string> { "brokerType", "brokerProfilePath", "calibrationObservationsPath" });
        var options = JsonSerializer.Deserialize<LoadBalancingOptions>(source.Bytes, Json) ?? throw new InvalidDataException("Empty configuration.");
        var directory = Path.GetDirectoryName(source.Path)!;
        string Resolve(string value) => Path.GetFullPath(value, directory);
        if (!matrix)
        {
            if (options.BrokerProfiles != null || options.Ideal && options.BrokerProfilePath != null ||
                !options.Ideal && string.IsNullOrWhiteSpace(options.BrokerProfilePath))
                throw new InvalidDataException("run/validate require exactly one profile, or ideal with no profile.");
            var profile = options.Ideal ? null : ProfileSnapshot.Load(Resolve(options.BrokerProfilePath!), options.BrokerType);
            return new[] { Prepare(options, source, profile) };
        }
        if (options.BrokerProfilePath != null || options.Ideal && options.BrokerProfiles != null)
            throw new InvalidDataException("matrix does not accept a single profile, or profiles in ideal mode.");
        if (!options.Ideal && (options.BrokerProfiles == null || !options.BrokerProfiles.Select(p => p.BrokerType).SequenceEqual(BrokerTypes)))
            throw new InvalidDataException("matrix requires the four ordered brokerProfiles: Aedes, Mosquitto, ActiveMQ, EMQX.");
        var prepared = new List<PreparedExperiment>();
        string? compatibility = null;
        foreach (var broker in options.Ideal ? new[] { options.BrokerType } : BrokerTypes)
        {
            var input = options.BrokerProfiles?.Single(p => p.BrokerType == broker);
            var profile = input == null ? null : ProfileSnapshot.Load(Resolve(input.BrokerProfilePath), broker);
            if (profile != null)
            {
                var key = profile.CompatibilityKey();
                if (compatibility != null && compatibility != key)
                    throw new InvalidDataException("Matrix profile source commits, deployment/CPU modes, coverage and methods must match.");
                compatibility = key;
            }
            var observations = input == null ? null : InputSnapshot.Read(Resolve(input.CalibrationObservationsPath));
            if (profile != null && !string.Equals(profile.Provenance.GetProperty("observationsSha256").GetString(), observations!.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Calibration observations SHA-256 does not match profile provenance.");
            for (var qos = 0; qos <= 2; qos++)
            {
                var rate = observations == null ? options.OfferedRps : CalibrationRate(observations, broker, qos);
                foreach (var mode in Enum.GetValues<BalancingMode>())
                    foreach (var seed in options.Ideal ? new[] { 42 } : new[] { 42, 43, 44 })
                    {
                        var expanded = options with { BrokerType = broker, Mode = mode,
                            BackendCount = mode == BalancingMode.Single ? 1 : 4,
                            TotalPublishers = options.Ideal ? options.TotalPublishers : 64, PayloadBytes = 16,
                            PublishQos = qos, SubscribeQos = qos, Seed = seed, OfferedRps = rate,
                            BrokerProfiles = null };
                        prepared.Add(Prepare(expanded, source, profile, observations));
                    }
            }
        }
        return prepared;
    }

    public static PreparedExperiment Prepare(LoadBalancingOptions options, InputSnapshot configuration,
        ProfileSnapshot? profile = null, InputSnapshot? observations = null)
    {
        options.Validate();
        if (options.Ideal != (profile == null) || profile != null && profile.Profile.BrokerType != options.BrokerType)
            throw new InvalidDataException("Ideal/profile mode or broker identity mismatch.");
        var assignments = ConnectionPolicies.Assign(options.Mode, options.TotalPublishers, options.BackendCount);
        foreach (var count in assignments.GroupBy(n => n).Select(group => group.Count()))
            profile?.RequirePoint(options.PayloadBytes, options.PublishQos, count);
        return new(options, assignments, configuration, profile, observations);
    }

    private static double CalibrationRate(InputSnapshot source, string broker, int qos)
    {
        using var document = JsonDocument.Parse(source.Bytes);
        var point = document.RootElement.GetProperty("points").EnumerateArray().Single(p =>
            p.GetProperty("broker").GetString() == broker && p.GetProperty("messageBytes").GetInt32() == 16 &&
            p.GetProperty("qos").GetInt32() == qos && p.GetProperty("publishers").GetInt32() == 64);
        return point.GetProperty("attemptedRps").GetProperty("mean").GetDouble();
    }

    private static void CheckProperties(JsonElement element, HashSet<string> allowed)
    {
        var seen = new HashSet<string>();
        foreach (var property in element.EnumerateObject())
            if (!seen.Add(property.Name) || !allowed.Contains(property.Name))
                throw new InvalidDataException($"Unknown or duplicate configuration field: {property.Name}.");
    }
}
