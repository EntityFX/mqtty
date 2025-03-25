using EntityFX.MqttY.Contracts.Monitoring;

namespace EntityFX.MqttY.Contracts.Network
{
    public record Packet(
        string From, string To, 
        NodeType FromType, NodeType ToType, byte[] Payload, string Protocol, string? Category = null, 
        MonitoringScope? Scope = null)
    {
        private int ttl = 64;

        public int Ttl => ttl;

        public Guid Id { get; set; } = Guid.NewGuid();

        public int DecrementTtl()
        {
            Interlocked.Decrement(ref ttl);

            if (ttl < 0) Interlocked.Exchange(ref ttl, 0);

            return Ttl;
        }
    }
}
