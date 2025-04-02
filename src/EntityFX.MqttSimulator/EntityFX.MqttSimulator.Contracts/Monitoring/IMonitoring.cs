using EntityFX.MqttY.Contracts.Network;
using System.Collections.Immutable;

namespace EntityFX.MqttY.Contracts.Monitoring
{
    public interface IMonitoring
    {
        event EventHandler<MonitoringItem> Added;

        event EventHandler<MonitoringScope> ScopeStarted;

        event EventHandler<MonitoringScope> ScopeEnded;

        IEnumerable<MonitoringItem> Items { get; }

        IEnumerable<MonitoringItem> GetByFilter(MonitoringFilter filter);

        IImmutableDictionary<string, long> GetCountersByCategory();

        IImmutableDictionary<MonitoringType, long> GetCountersByMonitoringType();

        void Push(MonitoringType type, string message, string? category, string protocol, MonitoringScope? scope = null, int? ttl = null, int? queueLength = null);

        void Push(NetworkPacket packet, MonitoringType type, string message, string protocol, string? category, MonitoringScope? scope = null);

        void Push(INode from, INode to, byte[]? packet, MonitoringType type, string message, string protocol, string? category, 
            MonitoringScope? scope = null, int? ttl = null, int? queueLength = null);

        MonitoringScope? BeginScope(string scopeMessage, MonitoringScope? parent = null);

        void BeginScope(ref NetworkPacket packet, string scope);

        void EndScope(ref NetworkPacket packet);

        void EndScope(MonitoringScope? scope);

        void Tick();

        public long Ticks { get; }
    }
}
