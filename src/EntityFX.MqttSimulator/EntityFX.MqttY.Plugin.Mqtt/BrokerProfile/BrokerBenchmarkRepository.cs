using System.Text.Json;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;

namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile
{
    public sealed class BrokerBenchmarkRepository : IBrokerBenchmarkRepository
    {
        private const string ResourceName = "EntityFX.MqttY.Plugin.Mqtt.Data.broker-benchmark.v2.json";
        private readonly IReadOnlyDictionary<string, MqttBrokerProfile> _profiles;

        public BrokerBenchmarkRepository() : this(ReadEmbedded()) { }

        public BrokerBenchmarkRepository(string json)
        {
            _profiles = Parse(json);
        }

        public MqttBrokerProfile Get(string brokerType)
        {
            if (string.IsNullOrWhiteSpace(brokerType))
                throw new ArgumentException("Broker name is required.", nameof(brokerType));
            return _profiles.TryGetValue(brokerType, out var profile)
                ? profile
                : throw new KeyNotFoundException($"Unknown MQTT broker profile '{brokerType}'.");
        }

        private static string ReadEmbedded()
        {
            var assembly = typeof(BrokerBenchmarkRepository).Assembly;
            using var stream = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static IReadOnlyDictionary<string, MqttBrokerProfile> Parse(string json)
        {
            BrokerBenchmarkV2Dto dto;
            try
            {
                dto = JsonSerializer.Deserialize<BrokerBenchmarkV2Dto>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("Broker calibration JSON is empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Broker calibration JSON is invalid.", exception);
            }

            if (dto.SchemaVersion != 2)
                throw new InvalidDataException(
                    $"Unsupported broker calibration schemaVersion={dto.SchemaVersion}; expected 2.");
            if (dto.Brokers == null || dto.Brokers.Length == 0)
                throw new InvalidDataException("Broker calibration contains no brokers.");

            var profiles = new Dictionary<string, MqttBrokerProfile>(StringComparer.Ordinal);
            foreach (var broker in dto.Brokers)
            {
                if (string.IsNullOrWhiteSpace(broker.Name) || profiles.ContainsKey(broker.Name))
                    throw new InvalidDataException("Broker names must be non-empty and unique.");
                var messageSizes = new Dictionary<int, MqttMessageSizeProfile>();
                if (broker.MessageSizes == null)
                    throw new InvalidDataException($"Broker '{broker.Name}' contains no message sizes.");
                foreach (var message in broker.MessageSizes)
                {
                    if (messageSizes.ContainsKey(message.MessageBytes))
                        throw new InvalidDataException(
                            $"Broker '{broker.Name}' duplicates MessageBytes={message.MessageBytes}.");
                    var qosProfiles = new Dictionary<MqttQos, CalibratedMqttQosProfile>();
                    if (message.Qos == null)
                        throw new InvalidDataException(
                            $"Broker '{broker.Name}', MessageBytes={message.MessageBytes} contains no QoS data.");
                    foreach (var qos in message.Qos)
                    {
                        if (!Enum.IsDefined(typeof(MqttQos), qos.Qos) || qosProfiles.ContainsKey(qos.Qos))
                            throw new InvalidDataException(
                                $"Broker '{broker.Name}', MessageBytes={message.MessageBytes} has duplicate/unknown QoS.");
                        if (qos.Samples == null)
                            throw new InvalidDataException(
                                $"Broker '{broker.Name}', MessageBytes={message.MessageBytes}, QoS={(int)qos.Qos} contains no samples.");
                        qosProfiles[qos.Qos] = new CalibratedMqttQosProfile(qos.Samples);
                    }
                    messageSizes[message.MessageBytes] =
                        new MqttMessageSizeProfile(message.MessageBytes, qosProfiles);
                }

                var profile = new MqttBrokerProfile
                {
                    BrokerType = broker.Name,
                    MessageSizes = messageSizes
                };
                profile.Validate();
                profiles.Add(broker.Name, profile);
            }
            return profiles;
        }

        private sealed class BrokerBenchmarkV2Dto
        {
            public int SchemaVersion { get; set; }
            public string? GeneratedAtUtc { get; set; }
            public string? MqttYCommitSha { get; set; }
            public string? MqttBenchmarkCommitSha { get; set; }
            public Dictionary<string, string> InputCsvSha256 { get; set; } = new();
            public int RunCount { get; set; }
            public double WarmupSeconds { get; set; }
            public double MeasurementSeconds { get; set; }
            public int Repeats { get; set; }
            public BrokerDto[] Brokers { get; set; } = Array.Empty<BrokerDto>();
        }

        private sealed class BrokerDto
        {
            public string Name { get; set; } = string.Empty;
            public MessageSizeDto[] MessageSizes { get; set; } = Array.Empty<MessageSizeDto>();
        }

        private sealed class MessageSizeDto
        {
            public int MessageBytes { get; set; }
            public QosDto[] Qos { get; set; } = Array.Empty<QosDto>();
        }

        private sealed class QosDto
        {
            public MqttQos Qos { get; set; }
            public CalibratedMqttQosSample[] Samples { get; set; } = Array.Empty<CalibratedMqttQosSample>();
        }
    }
}
