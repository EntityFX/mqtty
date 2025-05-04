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
        
        public Dictionary<MqttPacketType, ValueCounter<double>> AvgRpsPacketTypeCounters { get; }

        public override IEnumerable<ICounter> Counters
        {
            get => PacketTypeCounters.Values.ToArray<GenericCounter>()
                .Concat(RefusedPacketTypeCounters.Values).Cast<ICounter>().ToArray()
                .Concat(RpsPacketTypeCounters.Values).ToArray()
                .Concat(AvgRpsPacketTypeCounters.Values).ToArray();
            set => base.Counters = value;
        }

        public MqttCounters(string name, TicksOptions ticksOptions)
            : base(name)
        {
            _ticksPerSecond = 1 / ticksOptions.TickPeriod.TotalSeconds;
            _ticksOptions = ticksOptions;

            PacketTypeCounters = Enum.GetValues<MqttPacketType>()
                .ToDictionary(k => k, v => new GenericCounter(
                    v.GetEnumDescription()
                ));

            RefusedPacketTypeCounters = Enum.GetValues<MqttPacketType>()
            .ToDictionary(k => k, v => new GenericCounter(
                v.GetEnumDescription() + "_Refused"
            ));

            RpsPacketTypeCounters = Enum.GetValues<MqttPacketType>()
            .ToDictionary(k => k, v => new ValueCounter<double>(
                v.GetEnumDescription() + "_Rps"
            ));

            AvgRpsPacketTypeCounters = Enum.GetValues<MqttPacketType>()
                .ToDictionary(k => k, v => new ValueCounter<double>(
                    v.GetEnumDescription() + "_AvgRps"
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

            var tickRps = _ticksPerSecond / ticksDiff;

            foreach (var rpsPaketPair in PacketTypeCounters)
            {
                var diff = rpsPaketPair.Value.Value - rpsPaketPair.Value.PreviousValue;

                if ( diff > 0)
                {
                    RpsPacketTypeCounters[rpsPaketPair.Key].Set(diff * tickRps);
                    var avg = RpsPacketTypeCounters[rpsPaketPair.Key].HistoryValues
                        .Average(hv => hv.Value);
                    AvgRpsPacketTypeCounters[rpsPaketPair.Key].Set(avg);
                }
            }
        }
    }
}
