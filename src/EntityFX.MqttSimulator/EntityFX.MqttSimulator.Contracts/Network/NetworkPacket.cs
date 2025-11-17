using EntityFX.MqttY.Contracts.NetworkLogger;
using static System.Formats.Asn1.AsnWriter;

namespace EntityFX.MqttY.Contracts.Network
{
    public record NetworkPacket(
        Guid Id,
        Guid? RequestId,
        string From, string To,
        NodeType FromType, NodeType ToType,
        byte[] Payload, string Protocol,
        int HeaderBytes,
        bool WillWait,
        string? Category = null,
        NetworkLoggerScope? Scope = null, object? Context = default)
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

    public record NetworkPacket<TContext>(
        Guid Id,
        Guid? RequestId,
        string From, string To,
        NodeType FromType, NodeType ToType,
        byte[] Payload, string Protocol,
        int HeaderBytes,
        bool WillWait,
        string? Category = null,
        NetworkLoggerScope? Scope = null, TContext? TypedContext = default) :
            NetworkPacket(Id, RequestId, From, To, FromType, ToType, Payload, Protocol,
        HeaderBytes, WillWait, Category, Scope, TypedContext);

}
