using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{
    public interface IMonitoring
    {
        event EventHandler<MonitoringItem> Added;

        event EventHandler<MonitoringScope> ScopeStarted;

        event EventHandler<MonitoringScope> ScopeEnded;

        IEnumerable<MonitoringItem> Items { get; }

        IEnumerable<MonitoringItem> GetByFilter(MonitoringFilter filter);

        void Push(MonitoringType type, string message, string? category, string protocol, MonitoringScope? scope = null, int? ttl = null, int? queueLength = null);

        void Push(Packet packet, MonitoringType type, string message, string protocol, string? category, MonitoringScope? scope = null);

        void Push(INode from, INode to, byte[]? packet, MonitoringType type, string message, string protocol, string? category, 
            MonitoringScope? scope = null, int? ttl = null, int? queueLength = null);

        MonitoringScope BeginScope(string scopeMessage, MonitoringScope? parent = null);

        void BeginScope(ref Packet packet, string scope);

        void EndScope(ref Packet packet);

        void EndScope(MonitoringScope? scope);

        void Tick();

        public long Ticks { get; }
    }
}
