using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Network;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Sockets;
using System.Reflection.Emit;
using System.Xml.Linq;

public class Monitoring : IMonitoring
{
    public event EventHandler<MonitoringItem>? Added;
    public event EventHandler<MonitoringScope>? ScopeStarted;
    public event EventHandler<MonitoringScope>? ScopeEnded;

    private readonly ConcurrentDictionary<Guid, MonitoringItem> _storage = new();

    private readonly ConcurrentDictionary<Guid, MonitoringScope> _scopes = new();

    private readonly ConcurrentDictionary<string, long> _countersByCategory = new();

    private readonly long[] _countersByMonitoringType = new long[Enum.GetNames(typeof(MonitoringType)).Length];

    private readonly bool scopesEnabled;
    private readonly TimeSpan simulationTickTime;
    private readonly MonitoringIgnoreOption ignore;

    public IEnumerable<MonitoringItem> Items => _storage.Values.Take(10000);

    public long Ticks => _tick;

    private int _scopesStarted = 0;

    private int _scopesEnded = 0;

    private long _tick = 0;

    private object _stdLock = new object();

    public Monitoring(bool scopesEnabled, TimeSpan simulationTickTime, MonitoringIgnoreOption ignore)
    {
        this.scopesEnabled = scopesEnabled;
        this.simulationTickTime = simulationTickTime;
        this.ignore = ignore;
    }

    public void Push(MonitoringType type, string message, string protocol, string? category,
        MonitoringScope? scope = null, int? ttl = null, int? queueLength = null)
    {
        Push(string.Empty, NodeType.Other, string.Empty, NodeType.Other,
            Array.Empty<byte>(), type, message, protocol, category, scope, ttl, queueLength);
    }

    private void Push(string from, NodeType fromType, string to, NodeType toType, byte[]? packet,
        MonitoringType type, string message, string protocol, string? category, MonitoringScope? scope = null, int? ttl = null, int? queueLength = null)
    {
        if (ValidateIgnore(protocol, category))
        {
            return;
        }

        var item = new MonitoringItem(
            Guid.NewGuid(), _tick,
            TimeSpan.FromTicks(simulationTickTime.Ticks * _tick),
            DateTimeOffset.Now, from,
            fromType, to, toType,
            (uint)(packet?.Length ?? 0), type, protocol, message, scope, category, ttl, queueLength);

        _storage.AddOrUpdate(item.Id, item, (id, item) => item);

        Interlocked.Increment(ref _countersByMonitoringType[(int)type]);

        if (category != null)
        {
            lock (_stdLock)
            {
                if (!_countersByCategory.ContainsKey(category))
                {
                    _countersByCategory[category] = 0;
                }
                _countersByCategory[category]++;
            }

        }

        scope?.Items.Add(item);

        Added?.Invoke(this, item);
    }

    private bool ValidateIgnore(string protocol, string? category)
    {
        if (ignore.Protocol?.Contains(protocol) == true)
        {
            return true;
        }

        if (category != null)
        {
            if (ignore.Category?.Contains(category) == true)
            {
                return true;
            }
        }

        return false;
    }

    public void Push(INode from, INode to, byte[]? packet, MonitoringType type, string message,
        string protocol, string? category, MonitoringScope? scope = null, int? ttl = null, int? queueLength = null)
    {
        Push(from.Name,
            from.NodeType, to.Name, to.NodeType, packet,
            type, message, protocol, category, scope, ttl, queueLength);
    }


    public void Push(EntityFX.MqttY.Contracts.Network.NetworkPacket packet, MonitoringType type, string message,
        string protocol, string? category, MonitoringScope? scope = null)
    {
        Push(packet.From,
            packet.FromType, packet.To, packet.ToType, packet.Payload,
            type, message, protocol, category, packet?.Scope ?? scope, packet?.Ttl);
    }

    public MonitoringScope? BeginScope(string scope, MonitoringScope? parent = null)
    {
        if (!scopesEnabled) return null;

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

    public void BeginScope(ref EntityFX.MqttY.Contracts.Network.NetworkPacket packet, string scope)
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

    public void EndScope(ref EntityFX.MqttY.Contracts.Network.NetworkPacket packet)
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

    public IEnumerable<MonitoringItem> GetByFilter(MonitoringFilter filter)
    {
        var result = Items;

        if (filter.ByDate != null)
        {
            result = result.Where(mi =>
            {
                var byDatePred = true;

                if (filter?.ByDate.From != null)
                {
                    byDatePred = byDatePred && mi.Date >= filter.ByDate.From;
                }

                if (filter?.ByDate.To != null)
                {
                    byDatePred = byDatePred && mi.Date <= filter.ByDate.To;
                }

                return byDatePred;
            });
        }

        if (filter.ByNodeType?.Any() == true)
        {
            result = result.Where(mi => filter.ByNodeType.Contains(mi.SourceType)
            || filter.ByNodeType.Contains(mi.DestinationType));
        }

        if (filter.ByProtocol?.Any() == true)
        {
            result = result.Where(mi => filter.ByProtocol.Contains(mi.Protocol));
        }

        if (filter.ByMonitoringType?.Any() == true)
        {
            result = result.Where(mi => filter.ByMonitoringType.Contains(mi.Type));
        }

        return result.Take(filter.Limit).ToArray();
    }

    public IImmutableDictionary<string, long> GetCountersByCategory()
    {
        return _countersByCategory.ToImmutableDictionary();
    }

    public IImmutableDictionary<MonitoringType, long> GetCountersByMonitoringType()
    {
        return _countersByMonitoringType
            .Select((v, i) => new KeyValuePair<MonitoringType, long>((MonitoringType)i, v))
            .ToImmutableDictionary();
    }
}
