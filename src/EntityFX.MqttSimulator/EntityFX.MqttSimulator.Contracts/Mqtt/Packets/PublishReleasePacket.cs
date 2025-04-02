namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{
    public class PublishReleasePacket : PacketBase, IFlowPacket, IEquatable<PublishReleasePacket>
    {
        public PublishReleasePacket(ushort packetId)
        {
            PacketId = packetId;
            Type = MqttPacketType.PublishRelease;
        }

        public ushort PacketId { get; }

        public bool Equals(PublishReleasePacket? other)
        {
            if (other == null)
                return false;

            return PacketId == other.PacketId;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            var publishRelease = obj as PublishReleasePacket;

            if (publishRelease == null)
                return false;

            return Equals(publishRelease);
        }

        public static bool operator ==(PublishReleasePacket? publishRelease, PublishReleasePacket? other)
        {
            if ((object?)publishRelease == null || (object?)other == null)
                return Object.Equals(publishRelease, other);

            return publishRelease.Equals(other);
        }

        public static bool operator !=(PublishReleasePacket? publishRelease, PublishReleasePacket? other)
        {
            if ((object?)publishRelease == null || (object?)other == null)
                return !Object.Equals(publishRelease, other);

            return !publishRelease.Equals(other);
        }

        public override int GetHashCode()
        {
            return PacketId.GetHashCode();
        }
    }
}
