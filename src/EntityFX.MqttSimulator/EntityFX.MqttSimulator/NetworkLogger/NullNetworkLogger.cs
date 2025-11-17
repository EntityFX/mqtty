using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using System.Collections.Immutable;

public class NullNetworkLogger : INetworkLogger
{
    public IEnumerable<NetworkLoggerItem> Items => Enumerable.Empty<NetworkLoggerItem>();

    public event EventHandler<NetworkLoggerItem>? Added;
    public event EventHandler<NetworkLoggerScope>? ScopeStarted;
    public event EventHandler<NetworkLoggerScope>? ScopeEnded;

    public NetworkLoggerScope? BeginScope(long tick, string scopeMessage, NetworkLoggerScope? parent = null)
    {
        return null;
    }

    public void BeginScope(long tick, ref NetworkPacket packet, string scope)
    {
    }

    public void BeginScope<TContext>(long tick, ref NetworkPacket<TContext> packet, string scope)
    {
    }

    public void EndScope(long tick, ref NetworkPacket packet)
    {
    }

    public void EndScope(long tick, NetworkLoggerScope? scope)
    {
    }

    public void EndScope<TContext>(long tick, ref NetworkPacket<TContext> packet)
    {
    }

    public IEnumerable<NetworkLoggerItem> GetByFilter(NetworkLoggerFilter filter)
    {
        return Enumerable.Empty<NetworkLoggerItem>();
    }

    public IImmutableDictionary<string, long> GetCountersByCategory()
    {
        return ImmutableDictionary<string, long>.Empty;
    }

    public IImmutableDictionary<NetworkLoggerType, long> GetCountersByMonitoringType()
    {
        return ImmutableDictionary<NetworkLoggerType, long>.Empty;
    }

    public void Push(long tick, NetworkLoggerType type, string message, string protocol, string? category, NetworkLoggerScope? scope = null, int? ttl = null, int? queueLength = null)
    {
    }

    public void Push(long tick, NetworkPacket packet, NetworkLoggerType type, string message, string protocol, string? category, NetworkLoggerScope? scope = null)
    {
    }

    public void Push(long tick, INode from, INode to, byte[]? packet, NetworkLoggerType type, string message, string protocol, string? category, NetworkLoggerScope? scope = null, int? ttl = null, int? queueLength = null)
    {
    }

    public void Push<TContext>(long tick, NetworkPacket<TContext> packet, NetworkLoggerType type, string message, string protocol, string? category, NetworkLoggerScope? scope = null)
    {
    }
}
