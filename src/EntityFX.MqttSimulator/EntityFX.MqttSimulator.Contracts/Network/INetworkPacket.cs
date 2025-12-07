using EntityFX.MqttY.Contracts.NetworkLogger;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetworkPacket
    {
        string? Category { get; set; }
        int OutgoingTicks { get; set; }
        string From { get; set; }
        NodeType FromType { get; set; }

        int FromIndex { get; set; }

        int HeaderBytes { get; set; }
        Guid Id { get; set; }
        int PacketBytes { get; }
        byte[] Payload { get; set; }
        string Protocol { get; set; }
        Guid? RequestId { get; set; }
        NetworkLoggerScope? Scope { get; set; }
        string To { get; set; }
        NodeType ToType { get; set; }

        int ToIndex { get; set; }

        int Ttl { get; init; }

        object? Context { get; set; }

        int DecrementTtl();

        int GetHashCode();
        string ToString();
    }
}