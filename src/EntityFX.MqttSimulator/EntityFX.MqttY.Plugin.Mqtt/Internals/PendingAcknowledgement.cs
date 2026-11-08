using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class PendingAcknowledgement
    {
        public MqttPacketType Type { get; set; }

        public ushort PacketId { get; set; }

        /// <summary>Tick when the acknowledgement marker was stored (used for retention).</summary>
        public long Tick { get; set; }
    }
}
