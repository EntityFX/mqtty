namespace EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets
{
    public class SubscribeAckPacket : PacketBase, IPacket, IEquatable<SubscribeAckPacket>
    {
        public SubscribeAckPacket()
        {
            
        }

        public SubscribeAckPacket(ushort packetId, params SubscribeReturnCode[] returnCodes)
        {
            PacketId = packetId;
            ReturnCodes = returnCodes;
            Type = MqttPacketType.SubscribeAck;
        }

        public ushort PacketId { get; }

        public SubscribeReturnCode[] ReturnCodes { get; } = new SubscribeReturnCode[0];

        public bool Equals(SubscribeAckPacket? other)
        {
            if (other == null)
                return false;

            return PacketId == other.PacketId &&
                ReturnCodes.SequenceEqual(other.ReturnCodes);
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            var subscribeAck = obj as SubscribeAckPacket;

            if (subscribeAck == null)
                return false;

            return Equals(subscribeAck);
        }

        public static bool operator ==(SubscribeAckPacket? subscribeAck, SubscribeAckPacket? other)
        {
            if ((object?)subscribeAck == null || (object?)other == null)
                return Object.Equals(subscribeAck, other);

            return subscribeAck.Equals(other);
        }

        public static bool operator !=(SubscribeAckPacket? subscribeAck, SubscribeAckPacket? other)
        {
            if ((object?)subscribeAck == null || (object?)other == null)
                return !Object.Equals(subscribeAck, other);

            return !subscribeAck.Equals(other);
        }

        public override int GetHashCode()
        {
            return PacketId.GetHashCode() + ReturnCodes.GetHashCode();
        }
    }
}
