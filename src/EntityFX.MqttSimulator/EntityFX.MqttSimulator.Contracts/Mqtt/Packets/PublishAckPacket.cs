namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{

    public class PublishAckPacket : PacketBase, IPacket, IEquatable<PublishAckPacket>
    {
        public PublishAckPacket(ushort packetId)
        {
            PacketId = packetId;
            Type = MqttPacketType.PublishAck;
        }

        public ushort PacketId { get; }

        public bool Equals(PublishAckPacket? other)
        {
            if (other == null)
                return false;

            return PacketId == other.PacketId;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            var publishAck = obj as PublishAckPacket;

            if (publishAck == null)
                return false;

            return Equals(publishAck);
        }

        public static bool operator ==(PublishAckPacket? publishAck, PublishAckPacket? other)
        {
            if ((object?)publishAck == null || (object?)other == null)
                return Object.Equals(publishAck, other);

            return publishAck.Equals(other);
        }

        public static bool operator !=(PublishAckPacket? publishAck, PublishAckPacket? other)
        {
            if ((object?)publishAck == null || (object?)other == null)
                return !Object.Equals(publishAck, other);

            return !publishAck.Equals(other);
        }

        public override int GetHashCode()
        {
            return PacketId.GetHashCode();
        }
    }
}
