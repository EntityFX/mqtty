using System.Collections.Concurrent;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;

namespace EntityFX.MqttY.Plugin.Mqtt.Admission
{
    /// <summary>
    /// Profile-driven admission: rate limiting, probabilistic publish failure and
    /// processing latency. It owns the per-(message, QoS, clients) limiter cache,
    /// so <see cref="MqttBroker"/> only needs the resulting decision.
    /// </summary>
    internal sealed class ProfileBrokerAdmissionModel : IBrokerAdmissionModel
    {
        private readonly MqttBrokerProfile _profile;
        private readonly TimeSpan _tickPeriod;
        private readonly int _randomSeed;
        private readonly string _brokerName;
        private readonly int? _profileClientCount;
        private readonly Func<long?> _protocolFloorTicks;
        private readonly Func<bool> _capacityPacing;
        private readonly ConcurrentDictionary<(int MessageBytes, MqttQos Qos, int Clients), BrokerRateLimiter>
            _rateLimiters = new();

        public ProfileBrokerAdmissionModel(
            MqttBrokerProfile profile,
            TimeSpan tickPeriod,
            int randomSeed,
            string brokerName,
            int? profileClientCount,
            Func<long?> protocolFloorTicks,
            Func<bool> capacityPacing)
        {
            _profile = profile;
            _tickPeriod = tickPeriod;
            _randomSeed = randomSeed;
            _brokerName = brokerName;
            _profileClientCount = profileClientCount;
            _protocolFloorTicks = protocolFloorTicks;
            _capacityPacing = capacityPacing;
        }

        public BrokerAdmission TryAdmit(in PublishAdmissionContext context)
        {
            var clients = _profileClientCount ?? Math.Max(1, context.ClientCount);
            var sample = _profile.For(context.MessageBytes, context.Qos, clients);

            var limiterKey = (context.MessageBytes, context.Qos, clients);
            var limiter = _rateLimiters.GetOrAdd(limiterKey,
                _ => new BrokerRateLimiter(sample.CapacityRps, _tickPeriod));

            var capacityPacing = _capacityPacing();
            var capacityWaitTicks = capacityPacing ? limiter.Reserve(context.Tick) : 0;
            if (!capacityPacing && !limiter.TryAcquire(context.Tick))
            {
                return new BrokerAdmission(false, 0, BrokerRejectReason.RateLimit, 0, Immediate: false);
            }

            if (sample.PublishFailureRate > 0 &&
                DeterministicMqttSampler.Uniform(_randomSeed, _brokerName, context.Publisher,
                    context.LocalPublishSequence, "publish-failure") < sample.PublishFailureRate)
            {
                return new BrokerAdmission(false, 0, BrokerRejectReason.PublishFailure, 0, Immediate: false);
            }

            var processingTicks = GetProcessingTicks(sample, context.LocalPublishSequence, context.Publisher);
            // The observed end-to-end CDF already includes native queueing. Capacity
            // eligibility is an additional lower bound, not an independent delay to add twice.
            var delayTicks = Math.Max(processingTicks, capacityWaitTicks);

            return new BrokerAdmission(true, delayTicks, BrokerRejectReason.None,
                sample.ConditionalDeliveryLossRate, Immediate: false);
        }

        public void ClearRateLimiters() => _rateLimiters.Clear();

        private int GetProcessingTicks(CalibratedMqttQosSample sample,
            long localPublishSequence, string publisher)
        {
            var protocolFloor = _protocolFloorTicks();

            if (sample.ObservedLatencyDistribution != null && protocolFloor.HasValue)
            {
                var uniform = DeterministicMqttSampler.Uniform(_randomSeed, _brokerName, publisher,
                    localPublishSequence, "processing-latency");
                return checked((int)Math.Max(0,
                    EmpiricalTicksFromMs(sample.ObservedLatencyDistribution.Sample(uniform)) -
                    protocolFloor.Value));
            }

            if (sample.ProcessingLatencyDistribution != null)
            {
                var uniform = DeterministicMqttSampler.Uniform(_randomSeed, _brokerName, publisher,
                    localPublishSequence, "processing-latency");
                return EmpiricalTicksFromMs(sample.ProcessingLatencyDistribution.Sample(uniform));
            }

            if (sample.ProcessingLatencyQuantiles != null)
            {
                var uniform = DeterministicMqttSampler.Uniform(_randomSeed, _brokerName, publisher,
                    localPublishSequence, "processing-latency");
                return TicksFromMs(DeterministicMqttSampler.InverseCdf(
                    uniform, sample.ProcessingLatencyQuantiles));
            }

            return 1;
        }

        private int TicksFromMs(double latencyMs)
        {
            if (_tickPeriod <= TimeSpan.Zero)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Ceiling(latencyMs / _tickPeriod.TotalMilliseconds));
        }

        private int EmpiricalTicksFromMs(double latencyMs)
        {
            var ticks = Math.Ceiling(latencyMs / _tickPeriod.TotalMilliseconds);
            if (!double.IsFinite(ticks) || ticks < 0 || ticks > int.MaxValue)
                throw new InvalidDataException("Empirical latency cannot be represented at the selected tick resolution.");
            return (int)ticks;
        }
    }
}
