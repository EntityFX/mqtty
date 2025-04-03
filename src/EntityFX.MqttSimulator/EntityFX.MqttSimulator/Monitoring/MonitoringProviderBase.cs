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

    protected string GetMonitoringLine(MonitoringItem item) => $"{new string(' ', (item.Scope?.Level + 1 ?? 0) * 4)}<{item.Date:u}> " +
            $"(Tick={item.Tick}, Time={item.SimulationTime}) " +
            $"{{{item.Type}}} " +
            $"{(!string.IsNullOrEmpty(item.Category) ? $"[Category={item.Category}] " : "")}" +
            $"{(item.Ttl != null ? $"{{Ttl={item.Ttl}}} " : "")} " +
            $"{(item.QueueLength != null ? $"{{Queue={item.QueueLength}}} " : "")} " +
            $"{item.SourceType}[\"{item.From}\"] -> {item.DestinationType}[\"{item.To}\"]" +
            $"{(item.PacketSize > 0 ? $", NetworkMonitoringPacket Size={item.PacketSize}" : "")}" +
            $"{(!string.IsNullOrEmpty(item.Message) ? $", Message={item.Message}" : "")}";
}
