using System.Text;
using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.MqttY.LoadBalancing;

public sealed record ProfileSnapshot(InputSnapshot Source, MqttBrokerProfile Profile, JsonElement Provenance)
{
    public string CompatibilityKey()
    {
        var identity = Provenance.GetProperty("observations").GetProperty("identity");
        return string.Join("/", identity.GetProperty("cpuMode").GetString(), identity.GetProperty("deploymentMode").GetString(),
            identity.GetProperty("mqttBenchmarkCommitSha").GetString(), Provenance.GetProperty("mqttYCommitSha").GetString(),
            Provenance.TryGetProperty("coverageProtocol", out var coverage) ? coverage.GetString() : "full-v3",
            Profile.RequireObservedPublisherCount, Provenance.GetProperty("method").GetString());
    }

    public static ProfileSnapshot Load(string path, string broker)
    {
        var source = InputSnapshot.Read(path);
        using var document = JsonDocument.Parse(source.Bytes);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() is not (3 or 4) ||
            root.GetProperty("artifactType").GetString() is not ("broker-calibration" or "broker-calibration-compact-v1" or "broker-calibration-empirical-v1"))
            throw new InvalidDataException("An external v3/v4 broker-calibration profile is required.");
        var profile = new BrokerBenchmarkRepository(new UTF8Encoding(false, true).GetString(source.Bytes)).Get(broker);
        return new(source, profile, root.GetProperty("provenance").Clone());
    }

    public void RequirePoint(int bytes, int qos, int publishers)
    {
        Profile.For(bytes, (MqttQos)qos, publishers);
        if (!Profile.MessageSizes[bytes].Qos[(MqttQos)qos].Samples.Any(sample => sample.Clients == publishers))
            throw new InvalidDataException($"An exact observed profile point is required: bytes={bytes}, QoS={qos}, publishers={publishers}.");
    }
}
