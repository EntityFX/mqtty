namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{
    public class PublishPacket : PacketBase, IPacket, IEquatable<PublishPacket>
    {
        public PublishPacket(string topic, MqttQos qualityOfService, bool retain, bool duplicated, ushort? packetId = null)
        {
            QualityOfService = qualityOfService;
            Duplicated = duplicated;
            Retain = retain;
            Topic = topic;
            PacketId = packetId;
            Type = MqttPacketType.Publish;
        }


        public MqttQos QualityOfService { get; }

        public bool Duplicated { get; }

        public bool Retain { get; }

        public string Topic { get; }

        public ushort? PacketId { get; }

        public byte[] Payload { get; set; }

        public bool Equals(PublishPacket? other)
        {
            if (other == null)
                return false;

            var equals = QualityOfService == other.QualityOfService &&
                Duplicated == other.Duplicated &&
                Retain == other.Retain &&
                Topic == other.Topic &&
                PacketId == other.PacketId;

            if (Payload != null)
            {
                equals &= Payload.ToList().SequenceEqual(other.Payload);
            }

            return equals;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            var publish = obj as PublishPacket;

            if (publish == null)
                return false;

            return Equals(publish);
        }

        public static bool operator ==(PublishPacket? publish, PublishPacket? other)
        {
            if ((object?)publish == null || (object?)other == null)
                return Object.Equals(publish, other);

            return publish.Equals(other);
        }

        public static bool operator !=(PublishPacket? publish, PublishPacket? other)
        {
            if ((object?)publish == null || (object?)other == null)
                return !Object.Equals(publish, other);

            return !publish.Equals(other);
        }

        public override int GetHashCode()
        {
            var hashCode = QualityOfService.GetHashCode() +
                Duplicated.GetHashCode() +
                Retain.GetHashCode() +
                Topic.GetHashCode();

            if (Payload != null)
            {
                hashCode += BitConverter.ToString(Payload).GetHashCode();
            }

            if (PacketId.HasValue)
            {
                hashCode += PacketId.Value.GetHashCode();
            }

            return hashCode;
        }
    }
}
