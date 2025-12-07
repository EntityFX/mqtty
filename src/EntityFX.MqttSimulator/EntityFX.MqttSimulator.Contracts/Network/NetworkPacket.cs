using EntityFX.MqttY.Contracts.NetworkLogger;
using static System.Formats.Asn1.AsnWriter;

namespace EntityFX.MqttY.Contracts.Network
{
    public record struct NetworkPacket<TContext>(
        Guid Id,
        Guid? RequestId,
        string From, string To,
        NodeType FromType, NodeType ToType,
        int FromIndex, int ToIndex,
        byte[] Payload, string Protocol,
        int HeaderBytes,
        int OutgoingTicks,
        int Ttl = 64,
        string? Category = null,
        NetworkLoggerScope? Scope = null, TContext? TypedContext = default) : INetworkPacket
    {
        private int ttl = Ttl;

        public int Ttl { get => ttl; init => ttl = value; }

        public int PacketBytes => HeaderBytes + Payload.Length;

        public object Context { get => TypedContext; set => TypedContext = (TContext?)value; }

        public int DecrementTtl()
        {
            Interlocked.Decrement(ref ttl);

            if (ttl < 0) Interlocked.Exchange(ref ttl, 0);

            return Ttl;
        }
    }

    //public record NetworkPacket<TContext>(
    //    Guid Id,
    //    Guid? RequestId,
    //    string From, string To,
    //    NodeType FromType, NodeType ToType,
    //    byte[] Payload, string Protocol,
    //    int HeaderBytes,
    //    int DelayTicks,
    //    int Ttl = 64,
    //    string? Category = null,
    //    NetworkLoggerScope? Scope = null, TContext? TypedContext = default) :
    //        NetworkPacket(Id, RequestId, From, To, FromType, ToType, Payload, Protocol,
    //    HeaderBytes, DelayTicks, Ttl, Category, Scope, TypedContext);

}
