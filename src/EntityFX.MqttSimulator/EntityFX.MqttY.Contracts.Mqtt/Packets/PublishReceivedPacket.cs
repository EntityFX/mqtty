namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{
    public class PublishReceivedPacket : PacketBase, IFlowPacket, IEquatable<PublishReceivedPacket>
    {
        public PublishReceivedPacket(ushort packetId)
        {
            PacketId = packetId;
            Type = MqttPacketType.PublishReceived;
        }

        public ushort PacketId { get; }

        public bool Equals(PublishReceivedPacket? other)
        {
            if (other == null)
                return false;

            return PacketId == other.PacketId;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            var publishReceived = obj as PublishReceivedPacket;

            if (publishReceived == null)
                return false;

            return Equals(publishReceived);
        }

        public static bool operator ==(PublishReceivedPacket? publishReceived, PublishReceivedPacket? other)
        {
            if ((object?)publishReceived == null || (object?)other == null)
                return Equals(publishReceived, other);

            return publishReceived.Equals(other);
        }

        public static bool operator !=(PublishReceivedPacket? publishReceived, PublishReceivedPacket? other)
        {
            if ((object?)publishReceived == null || (object?)other == null)
                return !Equals(publishReceived, other);

            return !publishReceived.Equals(other);
        }

        public override int GetHashCode()
        {
            return PacketId.GetHashCode();
        }
    }
}
