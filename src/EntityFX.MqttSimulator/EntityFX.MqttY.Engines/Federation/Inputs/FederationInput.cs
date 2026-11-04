using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
namespace EntityFX.MqttY.Federation;

public sealed record FederationInput(FederationOptions Options, byte[] Configuration, byte[]? ProfileBytes,
    MqttBrokerProfile? Profile, string? ProfileSha256)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) }
    };
    public static FederationInput Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        Check(document.RootElement, typeof(FederationOptions));
        if (document.RootElement.TryGetProperty("network", out var network)) Check(network, typeof(FederationNetworkOptions));
        if (document.RootElement.TryGetProperty("links", out var links) && links.ValueKind != JsonValueKind.Null)
            foreach (var link in links.EnumerateArray()) Check(link, typeof(SiteLink));
        var options = JsonSerializer.Deserialize<FederationOptions>(bytes, Json) ?? throw new InvalidDataException("Empty config.");
        return Prepare(options, Path.GetDirectoryName(Path.GetFullPath(path))!, bytes);
    }
    public static FederationInput Prepare(FederationOptions options, string? directory = null, byte[]? configuration = null)
    {
        options.Validate();
        byte[]? bytes = null; MqttBrokerProfile? profile = null;
        if (!options.Ideal)
        {
            bytes = File.ReadAllBytes(Path.GetFullPath(options.BrokerProfilePath!, directory ?? Directory.GetCurrentDirectory()));
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.GetProperty("schemaVersion").GetInt32() is not (3 or 4)) throw new InvalidDataException("External v3/v4 profile required.");
            profile = new BrokerBenchmarkRepository(new UTF8Encoding(false, true).GetString(bytes)).Get(options.BrokerType);
            var counts = Enumerable.Range(0, options.Publishers).GroupBy(i => options.BrokerForSite(options.PublisherSite(i))).Select(g => g.Count());
            foreach (var count in counts)
            {
                profile.For(options.PayloadBytes, (MqttQos)options.Qos, count);
                if (!profile.MessageSizes[options.PayloadBytes].Qos[(MqttQos)options.Qos].Samples.Any(p => p.Clients == count))
                    throw new InvalidDataException("An exact observed publisher count is required: " + count);
            }
        }
        return new(options, configuration ?? JsonSerializer.SerializeToUtf8Bytes(options, Json), bytes, profile,
            bytes == null ? null : Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    private static void Check(JsonElement element, Type type)
    {
        var allowed = type.GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToHashSet();
        var seen = new HashSet<string>();
        foreach (var p in element.EnumerateObject()) if (!allowed.Contains(p.Name) || !seen.Add(p.Name))
            throw new InvalidDataException("Unknown or duplicate configuration field: " + p.Name);
    }
    public FederationInput WithOptions(FederationOptions options)
    {
        options.Validate();
        if (options.Ideal != (Profile == null) || Profile != null && options.BrokerType != Profile.BrokerType)
            throw new InvalidDataException("Profile identity cannot change within a prepared experiment.");
        if (Profile != null)
            foreach (var count in Enumerable.Range(0, options.Publishers).GroupBy(p => options.BrokerForSite(options.PublisherSite(p))).Select(g => g.Count()))
            {
                Profile.For(options.PayloadBytes, (MqttQos)options.Qos, count);
                if (!Profile.MessageSizes[options.PayloadBytes].Qos[(MqttQos)options.Qos].Samples.Any(p => p.Clients == count))
                    throw new InvalidDataException("Missing exact profile point: " + count);
            }
        return this with { Options = options, Configuration = JsonSerializer.SerializeToUtf8Bytes(options, Json) };
    }
}
