using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{
    public record MonitoringItem(
        Guid Id, DateTimeOffset Date, 
        string From, 
        NodeType SourceType,
        string To,
        NodeType DestinationType,
        uint PacketSize, MonitoringType Type, string Protocol, object Details, string? Category);
}
