namespace EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets
{

    public class ConnectPacket : PacketBase, IPacket, IEquatable<ConnectPacket>
    {
        public ConnectPacket(string clientId, bool cleanSession)
        {
            if (string.IsNullOrEmpty(clientId))
            {
                throw new ArgumentNullException(nameof(clientId));
            }

            ClientId = clientId;
            CleanSession = cleanSession;
            KeepAlive = 0;
            Type = MqttPacketType.Connect;
        }


        public string ClientId { get; set; } = string.Empty;

        public bool CleanSession { get; set; }

        public ushort KeepAlive { get; set; }

        //public MqttLastWill Will { get; set; }

        public string? UserName { get; set; }

        public string? Password { get; set; }

        public bool Equals(ConnectPacket? other)
        {
            if (other == null)
                return false;

            return ClientId == other.ClientId &&
                CleanSession == other.CleanSession &&
                KeepAlive == other.KeepAlive &&
                //Will == other.Will &&
                UserName == other.UserName &&
                Password == other.Password;
        }

        public override bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            var connect = obj as ConnectPacket;

            if (connect == null)
                return false;

            return Equals(connect);
        }

        public static bool operator ==(ConnectPacket? connect, ConnectPacket? other)
        {
            if ((object?)connect == null || (object?)other == null)
                return Object.Equals(connect, other);

            return connect.Equals(other);
        }

        public static bool operator !=(ConnectPacket? connect, ConnectPacket? other)
        {
            if ((object?)connect == null || (object?)other == null)
                return !Object.Equals(connect, other);

            return !connect.Equals(other);
        }

        public override int GetHashCode()
        {
            return ClientId?.GetHashCode() ?? 0;
        }
    }
}
