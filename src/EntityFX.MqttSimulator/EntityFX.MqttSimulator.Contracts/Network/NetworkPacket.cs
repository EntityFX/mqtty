using EntityFX.MqttY.Contracts.NetworkLogger;

namespace EntityFX.MqttY.Contracts.Network
{
    public record NetworkPacket(
        string From, string To, 
        NodeType FromType, NodeType ToType, byte[] Payload, string Protocol, string? Category = null, 
        NetworkLoggerScope? Scope = null)
    {
        private int ttl = 64;

        public int Ttl => ttl;

        public Guid Id { get; set; }

        public Guid? RequestId { get; set; }

        public int HeaderBytes { get; set; }

        public int PacketBytes => HeaderBytes + Payload.Length;

        public int DecrementTtl()
        {
            Interlocked.Decrement(ref ttl);

            if (ttl < 0) Interlocked.Exchange(ref ttl, 0);

            return Ttl;
        }
    }
}
