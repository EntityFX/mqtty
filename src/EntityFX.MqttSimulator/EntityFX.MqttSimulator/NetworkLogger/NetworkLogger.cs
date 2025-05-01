using EntityFX.MqttY.Collections;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using System.Collections.Concurrent;
using System.Collections.Immutable;

public class NetworkLogger : INetworkLogger
{
    public event EventHandler<NetworkLoggerItem>? Added;
    public event EventHandler<NetworkLoggerScope>? ScopeStarted;
    public event EventHandler<NetworkLoggerScope>? ScopeEnded;

    private readonly FixedSizedQueue<NetworkLoggerItem> _storage = new(10000);

    private readonly ConcurrentDictionary<Guid, NetworkLoggerScope> _scopes = new();

    private readonly ConcurrentDictionary<string, long> _countersByCategory = new();

    private readonly long[] _countersByMonitoringType = new long[Enum.GetNames(typeof(NetworkLoggerType)).Length];

    private readonly bool _scopesEnabled;
    private readonly TimeSpan _simulationTickTime;
    private readonly MonitoringIgnoreOption _ignore;

    public IEnumerable<NetworkLoggerItem> Items => _storage;

    private int _scopesStarted = 0;

    private int _scopesEnded = 0;

    private object _stdLock = new object();

    public NetworkLogger(bool scopesEnabled, TimeSpan simulationTickTime, MonitoringIgnoreOption ignore)
    {
        this._scopesEnabled = scopesEnabled;
        this._simulationTickTime = simulationTickTime;
        this._ignore = ignore;
    }

    public void Push(long tick, NetworkLoggerType type, string message, string protocol, string? category,
        NetworkLoggerScope? scope = null, int? ttl = null, int? queueLength = null)
    {
        Push(tick, string.Empty, NodeType.Other, string.Empty, NodeType.Other,
            Array.Empty<byte>(), type, message, protocol, category, scope, ttl, queueLength);
    }

    private void Push(long tick, string from, NodeType fromType, string to, NodeType toType, byte[]? packet,
        NetworkLoggerType type, string message, string protocol, string? category, NetworkLoggerScope? scope = null, int? ttl = null, int? queueLength = null)
    {
        if (ValidateIgnore(protocol, category))
        {
            return;
        }

        var item = new NetworkLoggerItem(
            Guid.NewGuid(), tick,
            TimeSpan.FromTicks(_simulationTickTime.Ticks * tick),
            DateTimeOffset.Now, from,
            fromType, to, toType,
            (uint)(packet?.Length ?? 0), type, protocol, message, scope, category, ttl, queueLength);

        _storage.Enqueue(item);

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
        if (_ignore.Protocol?.Contains(protocol) == true)
        {
            return true;
        }

        if (category != null)
        {
            if (_ignore.Category?.Contains(category) == true)
            {
                return true;
            }
        }

        return false;
    }

    public void Push(long tick, INode from, INode to, byte[]? packet, NetworkLoggerType type, string message,
        string protocol, string? category, NetworkLoggerScope? scope = null, int? ttl = null, int? queueLength = null)
    {
        Push(tick, from.Name,
            from.NodeType, to.Name, to.NodeType, packet,
            type, message, protocol, category, scope, ttl, queueLength);
    }


    public void Push(long tick, NetworkPacket packet, NetworkLoggerType type, string message,
        string protocol, string? category, NetworkLoggerScope? scope = null)
    {
        Push(tick, packet.From,
            packet.FromType, packet.To, packet.ToType, packet.Payload,
            type, message, protocol, category, packet?.Scope ?? scope, packet?.Ttl);
    }

    public NetworkLoggerScope? BeginScope(long tick, string scope, NetworkLoggerScope? parent = null)
    {
        if (!_scopesEnabled) return null;

        var scopeItem = new NetworkLoggerScope()
        {
            Id = Guid.NewGuid(),
            ScopeLabel = scope,
            Level = parent?.Level + 1 ?? 0,
            Date = DateTimeOffset.Now,
            Parent = parent,
            StartTick = tick,
            Source = parent?.Source,
            Destination = parent?.Destination
        };
        _scopes.AddOrUpdate(scopeItem.Id, scopeItem, (key, value) => scopeItem);
        (parent)?.Items.Add(scopeItem);

        Interlocked.Increment(ref _scopesStarted);
        ScopeStarted?.Invoke(this, scopeItem);
        return scopeItem!;
    }

    public void BeginScope(long tick, ref NetworkPacket packet, string scope)
    {
        if (!_scopesEnabled) return;

        if (packet.Scope == null)
        {
            var newScope = BeginScope(tick, scope, null);

            if (newScope == null) return;

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
            existingScope = BeginScope(tick, scope, null);
            packet = packet with
            {
                Scope = existingScope
            };
            return;
        }

        var linkedScope = BeginScope(tick, scope, existingScope);
        packet = packet with
        {
            Scope = linkedScope
        };
        return;
    }

    public void EndScope(long tick, ref NetworkPacket packet)
    {
        if (!_scopesEnabled) return;

        if (packet.Scope == null)
        {
            return;
        }

        var scope = packet.Scope;
        if (scope != null)
        {
            EndScope(tick, scope!);
        }
        packet = packet with
        {
            Scope = scope?.Parent
        };
        return;
    }

    public void EndScope(long tick, NetworkLoggerScope? scope)
    {
        if (!_scopesEnabled) return;

        if (scope == null)
        {
            return;
        }

        scope.ScopeStatus = ScopeStatus.End;
        scope.EndTick = tick;
        scope.Ticks = scope.EndTick - scope.StartTick;

        Interlocked.Increment(ref _scopesEnded);
        ScopeEnded?.Invoke(this, scope);

        return;
    }

    public IEnumerable<NetworkLoggerItem> GetByFilter(NetworkLoggerFilter filter)
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

        if (filter.ByType?.Any() == true)
        {
            result = result.Where(mi => filter.ByType.Contains(mi.Type));
        }

        return result.Take(filter.Limit).ToArray();
    }

    public IImmutableDictionary<string, long> GetCountersByCategory()
    {
        return _countersByCategory.ToImmutableDictionary();
    }

    public IImmutableDictionary<NetworkLoggerType, long> GetCountersByMonitoringType()
    {
        return _countersByMonitoringType
            .Select((v, i) => new KeyValuePair<NetworkLoggerType, long>((NetworkLoggerType)i, v))
            .ToImmutableDictionary();
    }
}
