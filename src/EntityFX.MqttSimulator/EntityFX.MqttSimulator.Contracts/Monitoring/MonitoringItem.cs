using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{

    public record MonitoringItem(
        Guid Id,
        long Tick,
        TimeSpan SimulationTime,
        DateTimeOffset Date,
        string From,
        NodeType SourceType,
        string To,
        NodeType DestinationType,
        uint PacketSize, MonitoringType Type,
        string Protocol, string Message, MonitoringScope? Scope, string? Category, int? Ttl, int? QueueLength) : IMonitoringItem
    {
        public MonitoringItemType MonitoringItemType => MonitoringItemType.Item;
    }
}
