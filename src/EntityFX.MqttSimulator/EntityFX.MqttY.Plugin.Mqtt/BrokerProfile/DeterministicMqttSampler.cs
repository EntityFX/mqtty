using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;

namespace EntityFX.MqttY.Plugin.Mqtt.BrokerProfile
{
    internal static class DeterministicMqttSampler
    {
        public static double Uniform(
            int seed, string brokerName, string clientId, long publishSequence, string purpose)
        {
            var key = string.Join("\n", seed.ToString(CultureInfo.InvariantCulture),
                brokerName, clientId, publishSequence.ToString(CultureInfo.InvariantCulture), purpose);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            var value = BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, sizeof(ulong)));
            return value / (double)ulong.MaxValue;
        }

        public static double InverseCdf(double uniform, LatencyQuantiles quantiles)
        {
            if (!double.IsFinite(uniform)) throw new ArgumentOutOfRangeException(nameof(uniform));
            uniform = Math.Clamp(uniform, 0, 1);
            var probabilities = new[] { 0.0, 0.50, 0.75, 0.95, 0.99, 1.0 };
            var values = new[] { quantiles.MinMs, quantiles.P50Ms, quantiles.P75Ms,
                quantiles.P95Ms, quantiles.P99Ms, quantiles.MaxMs };
            for (var index = 0; index < probabilities.Length - 1; index++)
            {
                if (uniform > probabilities[index + 1]) continue;
                var ratio = (uniform - probabilities[index]) /
                    (probabilities[index + 1] - probabilities[index]);
                return values[index] + ratio * (values[index + 1] - values[index]);
            }
            return values[^1];
        }
    }
}
