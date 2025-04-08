using EntityFX.MqttY.Contracts.Monitoring;

internal class NullMonitoringProvider : MonitoringProviderBase, IMonitoringProvider
{
    public NullMonitoringProvider(IMonitoring monitoring)
    : base(monitoring)
    {

    }

    protected override void WriteItem(MonitoringItem item)
    {

    }

    protected override void WriteScope(MonitoringScope scope)
    {

    }
}
