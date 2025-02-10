using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Mqtt.Internals
{
    internal class PendingAcknowledgement
    {
        public MqttPacketType Type { get; set; }

        public ushort PacketId { get; set; }
    }
}
