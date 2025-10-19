using EntityFX.MqttY.Contracts.NetworkLogger;

namespace EntityFX.MqttY.Contracts.Network
{
    public record struct NetworkPacket(
        Guid Id,
        Guid? RequestId,
        string From, string To, 
        NodeType FromType, NodeType ToType, 
        byte[] Payload, string Protocol,
        int HeaderBytes,
        bool WillWait,
        string? Category = null, 
        NetworkLoggerScope? Scope = null)
    {
        private int ttl = 64;

        public int Ttl => ttl;

        public int PacketBytes => HeaderBytes + Payload.Length;

        public int DecrementTtl()
        {
            Interlocked.Decrement(ref ttl);

            if (ttl < 0) Interlocked.Exchange(ref ttl, 0);

            return Ttl;
        }
    }
}
