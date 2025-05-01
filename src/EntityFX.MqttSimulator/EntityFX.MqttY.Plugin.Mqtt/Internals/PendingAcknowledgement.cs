using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class PendingAcknowledgement
    {
        public MqttPacketType Type { get; set; }

        public ushort PacketId { get; set; }
    }
}
