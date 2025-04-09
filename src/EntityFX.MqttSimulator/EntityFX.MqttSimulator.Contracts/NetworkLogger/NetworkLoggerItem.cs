using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.NetworkLogger
{

    public record NetworkLoggerItem(
        Guid Id,
        long Tick,
        TimeSpan SimulationTime,
        DateTimeOffset Date,
        string From,
        NodeType SourceType,
        string To,
        NodeType DestinationType,
        uint PacketSize, NetworkLoggerType Type,
        string Protocol, string Message, NetworkLoggerScope? Scope, string? Category, int? Ttl, int? QueueLength) : INetworkLoggerItem
    {
        public NetworkLoggerItemType ItemType => NetworkLoggerItemType.Item;
    }
}
