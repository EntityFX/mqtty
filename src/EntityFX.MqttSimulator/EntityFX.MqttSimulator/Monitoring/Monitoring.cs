using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Network;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Reflection.Emit;
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

    private long _tick = 0;

    public Monitoring(bool scopesEnabled)
    {
        this.scopesEnabled = scopesEnabled;
    }

    public void Push(MonitoringType type, string message, string protocol, string? category, MonitoringScope? scope = null, int? ttl = null)
    {
        var item = new MonitoringItem(
            Guid.NewGuid(), _tick, DateTimeOffset.Now, string.Empty,
                NodeType.Other, string.Empty, NodeType.Other,
            0, type, string.Empty, message, scope, category, ttl);

        _storage.TryAdd(item.Date, item);

        scope?.Items.Add(item);

        Added?.Invoke(this, item);
    }

    private void Push(string from, NodeType fromType, string to, NodeType toType, byte[]? packet,
        MonitoringType type, string message, string protocol, string? category, MonitoringScope? scope = null, int? ttl = null)
    {
        var item = new MonitoringItem(
            Guid.NewGuid(), _tick, DateTimeOffset.Now, from,
            fromType, to, toType,
            (uint)(packet?.Length ?? 0), type, protocol, message, scope, category, ttl);

        _storage.TryAdd(item.Date, item);

        scope?.Items.Add(item);

        Added?.Invoke(this, item);
    }

    public void Push(INode from, INode to, byte[]? packet, MonitoringType type, string message,
        string protocol, string? category, MonitoringScope? scope = null, int? ttl = null)
    {
        Push(from.Name,
            from.NodeType, to.Name, to.NodeType, packet,
            type, message, protocol, category, scope, ttl);
    }


    public void Push(Packet packet, MonitoringType type, string message,
        string protocol, string? category, MonitoringScope? scope = null)
    {
        Push(packet.From,
            packet.FromType, packet.To, packet.ToType, packet.Payload,
            type, message, protocol, category, packet?.Scope ?? scope, packet?.Ttl);
    }

    public MonitoringScope BeginScope(string scope, MonitoringScope? parent = null)
    {
        var scopeItem = new MonitoringScope()
        {
            Id = Guid.NewGuid(),
            ScopeLabel = scope,
            Level = parent?.Level + 1 ?? 0,
            Date = DateTimeOffset.Now,
            Parent = parent,
            StartTick = _tick,
            Source = parent?.Source,
            Destination = parent?.Destination
        };
        _scopes.AddOrUpdate(scopeItem.Id, scopeItem, (key, value) => scopeItem);
        (parent)?.Items.Add(scopeItem);

        Interlocked.Increment(ref _scopesStarted);
        ScopeStarted?.Invoke(this, scopeItem);
        return scopeItem!;
    }

    public void BeginScope(ref Packet packet, string scope)
    {
        if (!scopesEnabled) return;

        if (packet.Scope == null)
        {
            var newScope = BeginScope(scope, null);
            newScope.Source = packet.From;
            newScope.Destination = packet.To;
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

    public void EndScope(ref Packet packet)
    {
        if (!scopesEnabled) return;

        if (packet.Scope == null)
        {
            return;
        }

        var scope = packet.Scope;
        if (scope != null)
        {
            EndScope(scope!);
        }
        packet = packet with
        {
            Scope = scope?.Parent
        };
        return;
    }

    public void EndScope(MonitoringScope? scope)
    {
        if (!scopesEnabled) return;

        if (scope == null)
        {
            return;
        }

        scope.ScopeStatus = ScopeStatus.End;
        scope.EndTick = _tick;
        scope.Ticks = scope.EndTick - scope.StartTick;

        Interlocked.Increment(ref _scopesEnded);
        ScopeEnded?.Invoke(this, scope);

        return;
    }

    public void Tick()
    {
        Interlocked.Increment(ref _tick);
    }
}
