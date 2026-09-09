namespace EntityFX.MqttY.Contracts.Mqtt.BrokerProfile
{
    public sealed class CalibratedMqttQosProfile
    {
        public CalibratedMqttQosProfile(IReadOnlyList<CalibratedMqttQosSample> samples)
        {
            Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        }

        public IReadOnlyList<CalibratedMqttQosSample> Samples { get; }

        public CalibratedMqttQosSample Interpolate(int clients)
        {
            if (clients <= 0) throw new ArgumentOutOfRangeException(nameof(clients));
            if (Samples.Count == 0) throw new InvalidDataException("Calibration sample set is empty.");
            if (clients <= Samples[0].Clients) return Samples[0] with { Clients = clients };
            if (clients >= Samples[^1].Clients) return Samples[^1] with { Clients = clients };

            for (var index = 0; index < Samples.Count - 1; index++)
            {
                var lower = Samples[index];
                var upper = Samples[index + 1];
                if (clients < lower.Clients || clients > upper.Clients) continue;
                var ratio = (clients - lower.Clients) / (double)(upper.Clients - lower.Clients);
                return new CalibratedMqttQosSample(
                    clients,
                    Lerp(lower.CapacityRps, upper.CapacityRps, ratio),
                    Lerp(lower.PublishFailureRate, upper.PublishFailureRate, ratio),
                    Lerp(lower.ConditionalDeliveryLossRate, upper.ConditionalDeliveryLossRate, ratio),
                    Interpolate(lower.ProcessingLatencyQuantiles, upper.ProcessingLatencyQuantiles, ratio));
            }

            throw new InvalidDataException("Calibration samples are not strictly increasing by client count.");
        }

        private static LatencyQuantiles? Interpolate(
            LatencyQuantiles? lower, LatencyQuantiles? upper, double ratio)
        {
            if (lower == null && upper == null) return null;
            if (lower == null || upper == null)
                throw new InvalidDataException("Latency quantiles must be present consistently for a QoS profile.");
            return new LatencyQuantiles(
                Lerp(lower.MinMs, upper.MinMs, ratio),
                Lerp(lower.P50Ms, upper.P50Ms, ratio),
                Lerp(lower.P75Ms, upper.P75Ms, ratio),
                Lerp(lower.P95Ms, upper.P95Ms, ratio),
                Lerp(lower.P99Ms, upper.P99Ms, ratio),
                Lerp(lower.MaxMs, upper.MaxMs, ratio));
        }

        private static double Lerp(double lower, double upper, double ratio) =>
            lower + ratio * (upper - lower);
    }
}
