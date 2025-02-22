namespace EntityFX.MqttY.Contracts.Monitoring
{
    public interface IMonitoringItem {
        Guid Id { get; }

        MonitoringItemType MonitoringItemType { get; }
    }
}
