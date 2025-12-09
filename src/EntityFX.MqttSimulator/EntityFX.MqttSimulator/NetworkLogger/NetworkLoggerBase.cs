using EntityFX.MqttY.Contracts.NetworkLogger;

internal abstract class NetworkLoggerBase : INetworkLoggerProvider
{
    private readonly INetworkLogger _monitoring;
    private static object _lock = new object();

    public NetworkLoggerBase(INetworkLogger monitoring)
    {
        this._monitoring = monitoring;
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
        _monitoring.Added += (sender, e) =>
            ItemAdded(e);

        _monitoring.ScopeStarted += (sender, scope) =>
            ScopeStarted(scope);

        _monitoring.ScopeEnded += (sender, scope) =>
        {
            ScopeEnded(scope);
        };
    }

    protected abstract void WriteScope(NetworkLoggerScope scope);

    protected abstract void WriteItem(NetworkLoggerItem item);

    protected virtual string GetMonitoringLine(NetworkLoggerItem item) => item.GetMonitoringLine();
}
