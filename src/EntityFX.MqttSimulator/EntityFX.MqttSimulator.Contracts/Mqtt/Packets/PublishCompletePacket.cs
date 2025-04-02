namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{
    public class PublishCompletePacket : PacketBase, IFlowPacket, IEquatable<PublishCompletePacket>
    {
        public PublishCompletePacket(ushort packetId)
        {
            PacketId = packetId;
            Type = MqttPacketType.PublishComplete;
        }

        public ushort PacketId { get; }

        public bool Equals(PublishCompletePacket? other)
        {
            if (other == null)
                return false;

            return PacketId == other.PacketId;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            var publishComplete = obj as PublishCompletePacket;

            if (publishComplete == null)
                return false;

            return Equals(publishComplete);
        }

        public static bool operator ==(PublishCompletePacket? publishComplete, PublishCompletePacket? other)
        {
            if ((object?)publishComplete == null || (object?)other == null)
                return Object.Equals(publishComplete, other);

            return publishComplete.Equals(other);
        }

        public static bool operator !=(PublishCompletePacket? publishComplete, PublishCompletePacket? other)
        {
            if ((object?)publishComplete == null || (object?)other == null)
                return !Object.Equals(publishComplete, other);

            return !publishComplete.Equals(other);
        }

        public override int GetHashCode()
        {
            return PacketId.GetHashCode();
        }
    }
}
