using EntityFX.MqttY.Contracts.Network;
using System.Collections.Immutable;

namespace EntityFX.MqttY.Contracts.NetworkLogger
{
    public interface INetworkLogger
    {
        event EventHandler<NetworkLoggerItem> Added;

        event EventHandler<NetworkLoggerScope> ScopeStarted;

        event EventHandler<NetworkLoggerScope> ScopeEnded;

        IEnumerable<NetworkLoggerItem> Items { get; }

        IEnumerable<NetworkLoggerItem> GetByFilter(NetworkLoggerFilter filter);

        IImmutableDictionary<string, long> GetCountersByCategory();

        IImmutableDictionary<NetworkLoggerType, long> GetCountersByMonitoringType();

        void Push(long tick, NetworkLoggerType type, string message, string protocol, string? category, NetworkLoggerScope? scope = null, int? ttl = null, int? queueLength = null);

        void Push<TContext>(long tick, NetworkPacket<TContext> packet, NetworkLoggerType type, string message, string protocol, string? category, NetworkLoggerScope? scope = null);

        void Push(long tick, NetworkPacket packet, NetworkLoggerType type, string message, string protocol, string? category, NetworkLoggerScope? scope = null);

        void Push(long tick, INode from, INode to, byte[]? packet, NetworkLoggerType type, string message, string protocol, string? category,
            NetworkLoggerScope? scope = null, int? ttl = null, int? queueLength = null);

        NetworkLoggerScope? BeginScope(long tick, string scopeMessage, NetworkLoggerScope? parent = null);

        void BeginScope<TContext>(long tick, ref NetworkPacket<TContext> packet, string scope);

        void BeginScope(long tick, ref NetworkPacket packet, string scope);

        void EndScope<TContext>(long tick, ref NetworkPacket<TContext> packet);

        void EndScope(long tick, ref NetworkPacket packet);

        void EndScope(long tick, NetworkLoggerScope? scope);

    }
}
