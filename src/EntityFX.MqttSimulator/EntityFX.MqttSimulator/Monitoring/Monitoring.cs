using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;
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
    private readonly bool scopesEnabled;

    public IEnumerable<MonitoringItem> Items => _storage.Values.Take(10000);

    private int _scopesStarted = 0;

    private int _scopesEnded = 0;

    public Monitoring(bool scopesEnabled)
    {
        this.scopesEnabled = scopesEnabled;
    }

    private void Push(string from, NodeType fromType, string to, NodeType toType, byte[]? packet,
        MonitoringType type, string? category, MonitoringScope? scope = null)
    {
        var item = new MonitoringItem(
            Guid.NewGuid(), DateTimeOffset.Now, from,
            fromType, to, toType,
            (uint)(packet?.Length ?? 0), type, string.Empty, scope, category);

        _storage.TryAdd(item.Date, item);

        scope?.Items.Add(item);

        Added?.Invoke(this, item);
    }

    public void Push(INode from, INode to, byte[]? packet, MonitoringType type, string? category, MonitoringScope? scope = null)
    {
        Push(from.Address,
            from.NodeType, to.Address, to.NodeType, packet,
            type, category, scope);
    }


    public void Push(Packet packet, MonitoringType type,
        string? category, MonitoringScope? scope = null)
    {
        Push(packet.From,
            packet.FromType, packet.To, packet.ToType, packet.Payload,
            type, category, packet?.Scope ?? scope);
    }

    public MonitoringScope BeginScope(string scope, MonitoringScope? parent)
    {
        var scopeItem = new MonitoringScope()
        {
            Id = Guid.NewGuid(),
            ScopeLabel = scope,
            Level = parent?.Level + 1 ?? 0,
            Date = DateTimeOffset.Now,
            Parent = parent
        };
        _scopes.AddOrUpdate(scopeItem.Id, scopeItem, (key, value) => scopeItem);
        (parent)?.Items.Add(scopeItem);

        Interlocked.Increment(ref _scopesStarted);
        ScopeStarted?.Invoke(this, scopeItem);
        return scopeItem!;
    }

    public void TryBeginScope(ref Packet packet, string scope)
    {
        if (!scopesEnabled) return;

        if (packet.Scope == null)
        {
            var newScope = BeginScope(scope, null);
            packet = packet with
            {
                Scope = newScope
            };
            return;
        }

        var existingScope = packet.Scope;

        if (existingScope == null)
        {
            existingScope = BeginScope(scope, null);
            packet = packet with
            {
                Scope = existingScope
            };
            return;
        }

        var linkedScope = BeginScope(scope, existingScope);
        packet = packet with
        {
            Scope = linkedScope
        };
        return;
    }

    public MonitoringScope? EndScope(Guid? scopeId)
    {
        if (!scopesEnabled) return null;

        if (!_scopes.Any() || scopeId == null)
        {
            return null;
        }

        var currentScope = _scopes.GetValueOrDefault(scopeId.Value);

        if (currentScope == null)
        {
            return null;
        }
        ScopeEnded?.Invoke(this, currentScope);

        return currentScope;
    }

    public void TryEndScope(ref Packet packet)
    {
        if (!scopesEnabled) return;

        if (packet.Scope == null)
        {
            return;
        }

        var scope = packet.Scope;
        if (scope != null)
        {
            TryEndScope(scope!);
        }
        packet = packet with
        {
            Scope = scope?.Parent
        };
        return;
    }

    public void TryEndScope(MonitoringScope? scope)
    {
        if (!scopesEnabled) return;

        if (scope == null)
        {
            return;
        }

        scope.ScopeStatus = ScopeStatus.End;

        Interlocked.Increment(ref _scopesEnded);
        ScopeEnded?.Invoke(this, scope);

        return;
    }
}
