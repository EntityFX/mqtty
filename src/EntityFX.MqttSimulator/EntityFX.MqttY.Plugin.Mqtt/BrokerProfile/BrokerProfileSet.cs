using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Experiments;

namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

/// <summary>Loads exact artifact snapshots without merging their campaign provenance.</summary>
public sealed class BrokerProfileSet : IBrokerBenchmarkRepository
{
    private readonly Dictionary<string, MqttBrokerProfile> profiles = new(StringComparer.Ordinal);
    public IReadOnlyList<BrokerProfileArtifact> Artifacts { get; }
    public string Sha256 { get; }

    public BrokerProfileSet(IEnumerable<string> paths)
    {
        var artifacts = new List<BrokerProfileArtifact>();
        string? compatibility = null;
        foreach (var path in paths)
        {
            var bytes = File.ReadAllBytes(Path.GetFullPath(path));
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            var json = reader.ReadToEnd();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var repository = new BrokerBenchmarkRepository(json);
            JsonElement? provenance = null;
            IEnumerable<string> names;
            if (root.GetProperty("schemaVersion").GetInt32() is 3 or 4)
            {
                provenance = root.GetProperty("provenance").Clone();
                var identity = provenance.Value.GetProperty("observations").GetProperty("identity");
                var key = string.Join("/", identity.GetProperty("cpuMode").GetString(),
                    identity.GetProperty("deploymentMode").GetString(),
                    identity.GetProperty("mqttBenchmarkCommitSha").GetString(),
                    provenance.Value.GetProperty("mqttYCommitSha").GetString(),
                    provenance.Value.TryGetProperty("coverageProtocol", out var coverage) ? coverage.GetString() : "full-v3",
                    root.GetProperty("schemaVersion").GetInt32(), provenance.Value.GetProperty("method").GetString());
                if (compatibility != null && compatibility != key)
                    throw new InvalidDataException("Profile source commits, deployment and CPU modes must match.");
                compatibility = key;
                var pointsRoot = root.GetProperty("schemaVersion").GetInt32() == 4 ? root.GetProperty("calibration") : root;
                names = pointsRoot.GetProperty("points").EnumerateArray()
                    .Select(point => point.GetProperty("broker").GetString()!).Distinct(StringComparer.Ordinal);
            }
            else
                names = root.GetProperty("brokers").EnumerateArray().Select(broker => broker.GetProperty("name").GetString()!);
            foreach (var name in names)
                if (!profiles.TryAdd(name, repository.Get(name)))
                    throw new InvalidDataException($"Duplicate broker profile '{name}'.");
            artifacts.Add(new BrokerProfileArtifact(Path.GetFullPath(path),
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), provenance));
        }
        if (artifacts.Count == 0) throw new InvalidDataException("At least one profile is required.");
        if (artifacts.Count > 1 && artifacts.Any(artifact => artifact.Provenance == null))
            throw new InvalidDataException("Multiple profiles require calibrated v3 provenance.");
        Artifacts = artifacts.AsReadOnly();
        Sha256 = artifacts.Count == 1 ? artifacts[0].Sha256 :
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
                artifacts.Select(artifact => artifact.Sha256).OrderBy(hash => hash, StringComparer.Ordinal))))).ToLowerInvariant();
    }

    public MqttBrokerProfile Get(string brokerType) => profiles.TryGetValue(brokerType, out var profile)
        ? profile : throw new KeyNotFoundException($"Unknown broker '{brokerType}'.");
}
