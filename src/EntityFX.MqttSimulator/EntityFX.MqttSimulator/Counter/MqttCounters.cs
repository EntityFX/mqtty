using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Helper;
using System;
using System.Linq;

namespace EntityFX.MqttY.Counter
{
    internal class MqttCounters : CounterGroup
    {
        private readonly double ticksPerSecond;
        private readonly TicksOptions _ticksOptions;

        public Dictionary<MqttPacketType, GenericCounter> PacketTypeCounters { get; }

        public Dictionary<MqttPacketType, GenericCounter> RefusedPacketTypeCounters { get; }

        public Dictionary<MqttPacketType, ValueCounter<int>> RpsPacketTypeCounters { get; }

        public override IEnumerable<ICounter> Counters 
        {
            get => PacketTypeCounters.Values.ToArray()
                .Concat(RefusedPacketTypeCounters.Values).Cast<ICounter>().ToArray()
                .Concat(RpsPacketTypeCounters.Values).ToArray();
            set => base.Counters = value; 
        }

        public MqttCounters(string name, TicksOptions ticksOptions)
            : base(name)
        {
            ticksPerSecond = 1 / ticksOptions.TickPeriod.TotalSeconds;
            _ticksOptions = ticksOptions;

            PacketTypeCounters = Enum.GetValues<MqttPacketType>()
                .ToDictionary(k => k, v => new GenericCounter(
                    v.GetEnumDescription()
                ));

            RefusedPacketTypeCounters = Enum.GetValues<MqttPacketType>()
            .ToDictionary(k => k, v => new GenericCounter(
                v.GetEnumDescription()
            ));

            RpsPacketTypeCounters = Enum.GetValues<MqttPacketType>()
            .ToDictionary(k => k, v => new ValueCounter<int>(
                v.GetEnumDescription() + "_rps"
            ));

            
        }

        public void Increment(MqttPacketType mqttPacketType)
        {
            PacketTypeCounters[mqttPacketType].Increment();
        }
    }
}
