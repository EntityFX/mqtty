using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{
    public record MonitoringItemExtended<TDetails>
        (Guid Id, long Tick, DateTimeOffset Date, string From, NodeType SourceType, string To, NodeType DestinationType,
            uint PacketSize, MonitoringType Type, string Protocol, TDetails Details, 
            MonitoringScope? Scope, string? Category, int? Ttl)

        : MonitoringItem(Id, Tick, Date, From, SourceType, To, DestinationType, 
            PacketSize, Type, Protocol, Scope, Category, Ttl);
}
