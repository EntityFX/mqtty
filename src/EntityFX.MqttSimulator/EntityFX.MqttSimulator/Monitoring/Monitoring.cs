using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using System.Collections.Concurrent;
using System.Linq;
using System.Xml.Linq;
using static System.Formats.Asn1.AsnWriter;

public class Monitoring : IMonitoring
{
    public event EventHandler<MonitoringItem>? Added;
    public event EventHandler<MonitoringScope>? ScopeStarted;
    public event EventHandler<MonitoringScope>? ScopeEnded;

    private readonly ConcurrentDictionary<DateTimeOffset, MonitoringItem> _storage = new();

    private readonly ConcurrentDictionary<Guid, MonitoringScope> _scopes = new();

    public IEnumerable<MonitoringItem> Items => _storage.Values.Take(10000);

    private void Push(string from, NodeType fromType, string to, NodeType toType, byte[]? packet,
        MonitoringType type, string? category, Guid? scopeId)
    {
        var currentScope = scopeId != null ? _scopes.GetValueOrDefault(scopeId.Value) : null;
        var item = new MonitoringItem(
            Guid.NewGuid(), DateTimeOffset.Now, from,
            fromType, to, toType,
            (uint)(packet?.Length ?? 0), type, string.Empty, currentScope, category);

        _storage.TryAdd(item.Date, item);

        currentScope?.Items.Add(item);

        Added?.Invoke(this, item);
    }

    public void Push(INode from, INode to, byte[]? packet, MonitoringType type, string? category, Guid? scopeId = null)
    {
        Push(from.Address,
            from.NodeType, to.Address, to.NodeType, packet,
            type, category, scopeId);
    }


    public void Push(Packet packet, MonitoringType type,
        string? category)
    {
        Push(packet.From,
            packet.FromType, packet.To, packet.ToType, packet.Payload,
            type, category, packet.scope);
    }

    public MonitoringScope BeginScope(string scope)
    {
        var scopeItem = new MonitoringScope(Guid.NewGuid(), scope, 0, DateTimeOffset.Now, new List<MonitoringItem>());
        _scopes.AddOrUpdate(scopeItem.Id, scopeItem, (key, value) => scopeItem);
        ScopeStarted?.Invoke(this, scopeItem);
        return scopeItem!;
    }

    public MonitoringScope? EndScope(Guid? scopeId)
    {
        if (!_scopes.Any() || scopeId == null)
        {
            return null;
        }

        _scopes.TryRemove(scopeId.Value, out var currentScope);

        if (currentScope == null)
        {
            return null;
        }

        ScopeEnded?.Invoke(this, currentScope);
        return currentScope;
    }

    public MonitoringScope TryBeginScope(ref Packet packet, string scope)
    {
        if (packet.scope == null)
        {
            var newScope = BeginScope(scope);
            packet = packet with
            {
                scope = newScope.Id
            };
            return newScope;
        }

        var existingScope = _scopes.GetValueOrDefault(packet.scope.Value);

        if (existingScope == null)
        {
            existingScope = BeginScope(scope);
            packet = packet with
            {
                scope = existingScope.Id
            };
            return existingScope;
        }

        return existingScope;
    }

    public MonitoringScope? TryEndScope(ref Packet packet)
    {
        var scope = EndScope(packet.scope);
        packet = packet with
        {
            scope = null
        };
        return scope;
    }
}
