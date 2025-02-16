using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using System.Collections.Concurrent;

public class Monitoring : IMonitoring
{
    public event EventHandler<MonitoringItem>? Added;
    public event EventHandler<MonitoringScope>? ScopeStarted;
    public event EventHandler<MonitoringScope>? ScopeEnded;

    private readonly ConcurrentDictionary<DateTimeOffset, MonitoringItem> _storage = new();

    private readonly ConcurrentStack<MonitoringScope> _scopes = new();

    public IEnumerable<MonitoringItem> Items => _storage.Values.Take(10000);

    public void Push(string from, NodeType fromType, string to, NodeType toType, byte[]? packet, 
        MonitoringType type, string? category)
    {
        _scopes.TryPeek(out var currentScope);
        var item = new MonitoringItem(
            Guid.NewGuid(), DateTimeOffset.Now, from,
            fromType, to, toType,
            (uint)(packet?.Length ?? 0), type, string.Empty, currentScope, category);

        _storage.TryAdd(item.Date, item);

        Added?.Invoke(this, item);
    }


    public void Push(INode from, INode to, byte[]? packet, MonitoringType type, 
        string? category)
    {
        Push(from.Address,
            from.NodeType, to.Address, to.NodeType,
            packet, type, category);
    }

    public MonitoringScope BeginScope(string scope)
    {
        var scopeItem = new MonitoringScope(Guid.NewGuid(), scope, _scopes.Count, DateTimeOffset.Now);
        _scopes.Push(scopeItem);
        ScopeStarted?.Invoke(this, scopeItem);
        return scopeItem;
    }

    public MonitoringScope? EndScope()
    {
        if (!_scopes.Any())
        {
            return null;
        }

        if (!_scopes.TryPeek(out var currentScope))
        {
            return null;
        }
        ScopeEnded?.Invoke(this, currentScope);
        return currentScope;
    }
}
