namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    /// <summary>
    /// Полный эталонный профиль одного MQTT-брокера для трёх уровней QoS.
    /// </summary>
    public sealed class MqttBrokerProfile
    {
        public string BrokerType { get; init; } = string.Empty;

        public IDictionary<int, MqttMessageSizeProfile> MessageSizes { get; init; }
            = new Dictionary<int, MqttMessageSizeProfile>();

        public CalibratedMqttQosSample For(int messageBytes, MqttQos qos, int clients)
        {
            if (messageBytes <= 0) throw new ArgumentOutOfRangeException(nameof(messageBytes));
            if (clients <= 0) throw new ArgumentOutOfRangeException(nameof(clients));
            if (!Enum.IsDefined(typeof(MqttQos), qos)) throw new ArgumentOutOfRangeException(nameof(qos));
            if (!MessageSizes.TryGetValue(messageBytes, out var messageProfile))
                throw new KeyNotFoundException(
                    $"Broker '{BrokerType}' has no calibration for MessageBytes={messageBytes}.");
            if (!messageProfile.Qos.TryGetValue(qos, out var qosProfile))
                throw new KeyNotFoundException(
                    $"Broker '{BrokerType}' has no calibration for MessageBytes={messageBytes}, QoS={(int)qos}.");
            return qosProfile.Interpolate(clients);
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(BrokerType))
                throw new InvalidDataException("Broker name is required.");
            if (MessageSizes == null || MessageSizes.Count == 0)
                throw new InvalidDataException($"Broker '{BrokerType}' has no message-size calibrations.");

            foreach (var (messageBytes, messageProfile) in MessageSizes)
            {
                if (messageProfile == null || messageBytes <= 0 || messageProfile.MessageBytes != messageBytes)
                    throw new InvalidDataException($"Broker '{BrokerType}' has invalid MessageBytes={messageBytes}.");
                if (messageProfile.Qos == null)
                    throw new InvalidDataException(
                        $"Broker '{BrokerType}', MessageBytes={messageBytes} has no QoS calibrations.");
                foreach (var qos in Enum.GetValues<MqttQos>())
                {
                    if (!messageProfile.Qos.TryGetValue(qos, out var profile))
                        throw new InvalidDataException(
                            $"Broker '{BrokerType}', MessageBytes={messageBytes} is missing QoS={(int)qos}.");
                    ValidateSamples(messageBytes, qos, profile.Samples);
                }
                if (messageProfile.Qos.Keys.Any(qos => !Enum.IsDefined(typeof(MqttQos), qos)))
                    throw new InvalidDataException($"Broker '{BrokerType}' contains an unknown QoS.");
            }
        }

        private void ValidateSamples(
            int messageBytes, MqttQos qos, IReadOnlyList<CalibratedMqttQosSample> samples)
        {
            if (samples.Count == 0)
                throw new InvalidDataException(
                    $"Broker '{BrokerType}', MessageBytes={messageBytes}, QoS={(int)qos} has no samples.");
            var previousClients = 0;
            foreach (var sample in samples)
            {
                if (sample.Clients <= previousClients)
                    throw new InvalidDataException("Client counts must be unique, positive and strictly increasing.");
                previousClients = sample.Clients;
                if (!double.IsFinite(sample.CapacityRps) || sample.CapacityRps <= 0)
                    throw new InvalidDataException("CapacityRps must be finite and positive.");
                ValidateRate(sample.PublishFailureRate, nameof(sample.PublishFailureRate));
                ValidateRate(sample.ConditionalDeliveryLossRate, nameof(sample.ConditionalDeliveryLossRate));
                if (qos == MqttQos.AtMostOnce)
                {
                    if (sample.ProcessingLatencyQuantiles != null)
                        throw new InvalidDataException("QoS 0 processing latency must be null/notApplicable.");
                }
                else
                {
                    ValidateQuantiles(sample.ProcessingLatencyQuantiles);
                }
            }
        }

        private static void ValidateRate(double rate, string name)
        {
            if (!double.IsFinite(rate) || rate < 0 || rate > 1)
                throw new InvalidDataException($"{name} must be finite and in [0, 1].");
        }

        private static void ValidateQuantiles(LatencyQuantiles? quantiles)
        {
            if (quantiles == null) throw new InvalidDataException("QoS 1/2 latency quantiles are required.");
            var values = new[] { quantiles.MinMs, quantiles.P50Ms, quantiles.P75Ms,
                quantiles.P95Ms, quantiles.P99Ms, quantiles.MaxMs };
            if (values.Any(value => !double.IsFinite(value) || value < 0) ||
                !values.SequenceEqual(values.OrderBy(value => value)))
                throw new InvalidDataException(
                    "Latency quantiles must be finite and ordered min <= p50 <= p75 <= p95 <= p99 <= max.");
        }
    }
}
