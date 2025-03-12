using EntityFX.MqttY.Contracts.Monitoring;

internal abstract class MonitoringProviderBase : IMonitoringProvider
{
    private readonly IMonitoring monitoring;
    private static object _lock = new object();

    public MonitoringProviderBase(IMonitoring monitoring)
    {
        this.monitoring = monitoring;
    }

    public void ItemAdded(MonitoringItem item)
    {
        lock (_lock)
        {
            if (item.Scope != null) return;

            WriteItem(item);
        }
    }

    public void ScopeEnded(MonitoringScope scope)
    {
        lock (_lock)
        {
            WriteScope(scope);
        }
    }

    public void ScopeStarted(MonitoringScope scope)
    {

    }

    public void Start()
    {
        monitoring.Added += (sender, e) =>
            ItemAdded(e);

        monitoring.ScopeStarted += (sender, scope) =>
            ScopeStarted(scope);

        monitoring.ScopeEnded += (sender, scope) =>
        {
            ScopeEnded(scope);
        };
    }

    protected abstract void WriteScope(MonitoringScope scope);

    protected abstract void WriteItem(MonitoringItem item);
}
