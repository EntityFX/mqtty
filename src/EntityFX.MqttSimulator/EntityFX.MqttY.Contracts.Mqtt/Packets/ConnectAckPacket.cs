namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{
    public class ConnectAckPacket : PacketBase, IPacket, IEquatable<ConnectAckPacket>
    {
        public ConnectAckPacket()
        {
            Type = MqttPacketType.ConnectAck;
        }

        public ConnectAckPacket(MqttConnectionStatus status, bool existingSession)
            : this()
        {
            Status = status;
            SessionPresent = existingSession;
        }

        public MqttConnectionStatus Status { get; internal set; }

        public bool SessionPresent { get; internal set; }

        public bool Equals(ConnectAckPacket? other)
        {
            if (other == null)
                return false;

            return Status == other.Status &&
                SessionPresent == other.SessionPresent;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            var connectAck = obj as ConnectAckPacket;

            if (connectAck == null)
                return false;

            return Equals(connectAck);
        }

        public static bool operator ==(ConnectAckPacket? connectAck, ConnectAckPacket? other)
        {
            if ((object?)connectAck == null || (object?)other == null)
                return Equals(connectAck, other);

            return connectAck.Equals(other);
        }

        public static bool operator !=(ConnectAckPacket? connectAck, ConnectAckPacket? other)
        {
            if ((object?)connectAck == null || (object?)other == null)
                return !Equals(connectAck, other);

            return !connectAck.Equals(other);
        }

        public override int GetHashCode()
        {
            return Status.GetHashCode() + SessionPresent.GetHashCode();
        }
    }
}
