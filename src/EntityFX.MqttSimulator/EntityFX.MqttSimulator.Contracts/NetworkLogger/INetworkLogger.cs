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

        void Push(NetworkLoggerType type, string message, string protocol, string? category, NetworkLoggerScope? scope = null, int? ttl = null, int? queueLength = null);

        void Push(NetworkPacket packet, NetworkLoggerType type, string message, string protocol, string? category, NetworkLoggerScope? scope = null);

        void Push(INode from, INode to, byte[]? packet, NetworkLoggerType type, string message, string protocol, string? category,
            NetworkLoggerScope? scope = null, int? ttl = null, int? queueLength = null);

        NetworkLoggerScope? BeginScope(string scopeMessage, NetworkLoggerScope? parent = null);

        void BeginScope(ref NetworkPacket packet, string scope);

        void EndScope(ref NetworkPacket packet);

        void EndScope(NetworkLoggerScope? scope);

        void Tick();

        public long Ticks { get; }
    }
}
