using EntityFX.MqttY.Contracts.NetworkLogger;

internal abstract class NetworkLoggerBase : INetworkLoggerProvider
{
    private readonly INetworkLogger monitoring;
    private static object _lock = new object();

    public NetworkLoggerBase(INetworkLogger monitoring)
    {
        this.monitoring = monitoring;
    }

    public void ItemAdded(NetworkLoggerItem item)
    {
        lock (_lock)
        {
            if (item.Scope != null) return;

            WriteItem(item);
        }
    }

    public void ScopeEnded(NetworkLoggerScope scope)
    {
        lock (_lock)
        {
            WriteScope(scope);
        }
    }

    public void ScopeStarted(NetworkLoggerScope scope)
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

    protected abstract void WriteScope(NetworkLoggerScope scope);

    protected abstract void WriteItem(NetworkLoggerItem item);

    protected string GetMonitoringLine(NetworkLoggerItem item) => $"{new string(' ', (item.Scope?.Level + 1 ?? 0) * 4)}<{item.Date:O}> " +
            $"(Tick={item.Tick}, Time={item.SimulationTime}) " +
            $"{{{item.Type}}} " +
            $"{(!string.IsNullOrEmpty(item.Category) ? $"[Category={item.Category}] " : "")}" +
            $"{(item.Ttl != null ? $"{{Ttl={item.Ttl}}} " : "")} " +
            $"{(item.QueueLength != null ? $"{{Queue={item.QueueLength}}} " : "")} " +
            $"{item.SourceType}[\"{item.From}\"] -> {item.DestinationType}[\"{item.To}\"]" +
            $"{(item.PacketSize > 0 ? $", NetworkMonitoringPacket Size={item.PacketSize}" : "")}" +
            $"{(!string.IsNullOrEmpty(item.Message) ? $", Message={item.Message}" : "")}";
}
