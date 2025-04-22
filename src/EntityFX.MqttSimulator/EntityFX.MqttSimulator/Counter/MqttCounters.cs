using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Helper;
using System;

namespace EntityFX.MqttY.Counter
{
    internal class MqttCounters : CounterGroup
    {
        public Dictionary<MqttPacketType, GenericCounter> PacketTypeCounters { get; }

        public Dictionary<MqttPacketType, GenericCounter> RefusedPacketTypeCounters { get; }

        public override IEnumerable<ICounter> Counters 
        {
            get => PacketTypeCounters.Values.ToArray(); 
            set => base.Counters = value; 
        }

        public MqttCounters(string name)
            : base(name)
        {
            PacketTypeCounters = Enum.GetValues<MqttPacketType>()
                .ToDictionary(k => k, v => new GenericCounter(
                    v.GetEnumDescription()
                ));

        }

        public void Increment(MqttPacketType mqttPacketType)
        {
            PacketTypeCounters[mqttPacketType].Increment();
        }
    }
}
