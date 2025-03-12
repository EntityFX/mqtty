using EntityFX.MqttY.Contracts.Monitoring;

internal interface IMonitoringProvider
{
    void Start();

    void ItemAdded(MonitoringItem item);
    void ScopeEnded(MonitoringScope scope);
    void ScopeStarted(MonitoringScope scope);
}