using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{
    public record MonitoringItem(
        Guid Id, DateTimeOffset Date, 
        string From, 
        NodeType SourceType,
        string To,
        NodeType DestinationType,
        uint PacketSize, MonitoringType Type, 
        string Protocol, MonitoringScope? Scope, string? Category);

    public record MonitoringItemExtended<TDetails>
        (Guid Id, DateTimeOffset Date, string From, NodeType SourceType, string To, NodeType DestinationType,
            uint PacketSize, MonitoringType Type, string Protocol, TDetails Details, 
            MonitoringScope? Scope, string? Category)

        : MonitoringItem(Id, Date, From, SourceType, To, DestinationType, PacketSize, Type, Protocol, Scope, Category);
}
