using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{

    public record MonitoringItem(
        Guid Id,
        long Tick,
        DateTimeOffset Date,
        string From,
        NodeType SourceType,
        string To,
        NodeType DestinationType,
        uint PacketSize, MonitoringType Type,
        string Protocol, MonitoringScope? Scope, string? Category, int? Ttl) : IMonitoringItem
    {
        public MonitoringItemType MonitoringItemType => MonitoringItemType.Item;
    }
}
