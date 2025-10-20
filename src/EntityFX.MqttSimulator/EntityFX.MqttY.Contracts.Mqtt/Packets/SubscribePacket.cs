namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{
    public class SubscribePacket : PacketBase, IPacket, IEquatable<SubscribePacket>
    {
        public SubscribePacket(ushort packetId, Subscription[] subscriptions)
        {
            PacketId = packetId;
            Subscriptions = subscriptions;
            Type = MqttPacketType.Subscribe;
        }

        public ushort PacketId { get; }

        public Subscription[] Subscriptions { get; }

        public bool Equals(SubscribePacket? other)
        {
            if (other == null)
                return false;

            return PacketId == other.PacketId &&
                Subscriptions.SequenceEqual(other.Subscriptions);
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            var subscribe = obj as SubscribePacket;

            if (subscribe == null)
                return false;

            return Equals(subscribe);
        }

        public static bool operator ==(SubscribePacket? subscribe, SubscribePacket? other)
        {
            if ((object?)subscribe == null || (object?)other == null)
                return Equals(subscribe, other);

            return subscribe.Equals(other);
        }

        public static bool operator !=(SubscribePacket? subscribe, SubscribePacket? other)
        {
            if ((object?)subscribe == null || (object?)other == null)
                return !Equals(subscribe, other);

            return !subscribe.Equals(other);
        }

        public override int GetHashCode()
        {
            return PacketId.GetHashCode() + Subscriptions.GetHashCode();
        }
    }
}
