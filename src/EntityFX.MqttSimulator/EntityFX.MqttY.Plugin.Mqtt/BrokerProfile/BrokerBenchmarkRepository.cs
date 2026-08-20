using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;

namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile
{
    /// <summary>
    /// Загружает эталонные профили брокеров из встроенного ресурса broker-benchmark.json.
    /// </summary>
    public sealed class BrokerBenchmarkRepository : IBrokerBenchmarkRepository
    {
        private const string ResourceName = "EntityFX.MqttY.Plugin.Mqtt.Data.broker-benchmark.json";

        private readonly IReadOnlyDictionary<string, MqttBrokerProfile> _profiles;

        public BrokerBenchmarkRepository()
        {
            var assembly = typeof(BrokerBenchmarkRepository).Assembly;

            using var stream = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{ResourceName}' was not found.");

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            _profiles = Parse(json);
        }

        public MqttBrokerProfile? Get(string brokerType)
        {
            return _profiles.TryGetValue(brokerType, out var profile)
                ? profile
                : null;
        }

        private static IReadOnlyDictionary<string, MqttBrokerProfile> Parse(string json)
        {
            var dto = JsonSerializer.Deserialize<Dictionary<string, BrokerProfileDto>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (dto == null)
            {
                return new Dictionary<string, MqttBrokerProfile>();
            }

            return dto.ToDictionary(
                kv => kv.Key,
                kv => new MqttBrokerProfile
                {
                    BrokerType = kv.Key,
                    Qos0 = ToProfile(kv.Value.Qos0),
                    Qos1 = ToProfile(kv.Value.Qos1),
                    Qos2 = ToProfile(kv.Value.Qos2)
                });
        }

        private static MqttQosProfile ToProfile(IReadOnlyList<MqttQosSampleDto>? samples)
        {
            return new MqttQosProfile
            {
                Samples = (samples ?? Array.Empty<MqttQosSampleDto>())
                    .Select(s => new MqttQosSample(s.Clients, s.Rps, s.LatencyMs, s.FailRate))
                    .OrderBy(s => s.Clients)
                    .ToArray()
            };
        }

        private sealed class BrokerProfileDto
        {
            public MqttQosSampleDto[] Qos0 { get; set; } = Array.Empty<MqttQosSampleDto>();
            public MqttQosSampleDto[] Qos1 { get; set; } = Array.Empty<MqttQosSampleDto>();
            public MqttQosSampleDto[] Qos2 { get; set; } = Array.Empty<MqttQosSampleDto>();
        }

        private sealed class MqttQosSampleDto
        {
            public int Clients { get; set; }
            public double Rps { get; set; }
            public double LatencyMs { get; set; }
            public double FailRate { get; set; }
        }
    }
}