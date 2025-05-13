using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Counter;
using EntityFX.MqttY.Helper;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Counter
{
    internal class MqttCounters : CounterGroup
    {
        private readonly double _ticksPerSecond;
        private readonly TicksOptions _ticksOptions;
        private long _lastTicks;

        public Dictionary<MqttPacketType, GenericCounter> PacketTypeCounters { get; }

        public Dictionary<MqttPacketType, GenericCounter> RefusedPacketTypeCounters { get; }

        public Dictionary<MqttPacketType, ValueCounter<double>> RpsPacketTypeCounters { get; }

        public override IEnumerable<ICounter> Counters
        {
            get => PacketTypeCounters.Values.ToArray<GenericCounter>()
                .Concat(RefusedPacketTypeCounters.Values).Cast<ICounter>().ToArray()
                .Concat(RpsPacketTypeCounters.Values).ToArray();
            set => base.Counters = value;
        }

        public MqttCounters(string name, TicksOptions ticksOptions)
            : base(name)
        {
            _ticksPerSecond = 1 / ticksOptions.TickPeriod.TotalSeconds;
            _ticksOptions = ticksOptions;

            PacketTypeCounters = Enum.GetValues<MqttPacketType>()
                .ToDictionary(k => k, v => new GenericCounter(
                    v.GetEnumDescription(), ticksOptions.CounterHistoryDepth
                ));

            RefusedPacketTypeCounters = Enum.GetValues<MqttPacketType>()
            .ToDictionary(k => k, v => new GenericCounter(
                v.GetEnumDescription() + "_Refused", ticksOptions.CounterHistoryDepth
            ));

            RpsPacketTypeCounters = Enum.GetValues<MqttPacketType>()
            .ToDictionary(k => k, v => new ValueCounter<double>(
                v.GetEnumDescription() + "_Rps", ticksOptions.CounterHistoryDepth, "Rps"
            ));
        }

        public void Increment(MqttPacketType mqttPacketType)
        {
            PacketTypeCounters[mqttPacketType].Increment();
        }

        public void Refuse(MqttPacketType mqttPacketType)
        {
            RefusedPacketTypeCounters[mqttPacketType].Increment();
        }

        public override void Refresh(long totalTicks)
        {
            base.Refresh(totalTicks);

            var ticksDiff = totalTicks - _lastTicks;

            if (ticksDiff < 100) return;

            foreach (var rpsPaketPair in PacketTypeCounters)
            {
                var firstTick = rpsPaketPair.Value.TickFirstValue;

                if (firstTick == null)
                {
                    continue;
                }

                var rpsTickDiff = totalTicks - firstTick.Value.Key;
                var valueDiff = rpsPaketPair.Value.Value - firstTick.Value.Value;
                if (valueDiff == 0) {
                    continue;
                }

                var ticksRps = _ticksPerSecond / rpsTickDiff;

                RpsPacketTypeCounters[rpsPaketPair.Key].Set(ticksRps * valueDiff);
            }
        }
    }
}
