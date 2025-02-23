using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{
    public interface IMonitoring
    {
        public event EventHandler<MonitoringItem> Added;

        public event EventHandler<MonitoringScope> ScopeStarted;

        public event EventHandler<MonitoringScope> ScopeEnded;

        public IEnumerable<MonitoringItem> Items { get; }

        public void Push(MonitoringType type, string? category, MonitoringScope? scope = null, int? ttl = null);

        public void Push(Packet packet, MonitoringType type, string? category, MonitoringScope? scope = null);

        void Push(INode from, INode to, byte[]? packet, MonitoringType type, string? category, 
            MonitoringScope? scope = null, int? ttl = null);

        MonitoringScope BeginScope(string scopeMessage, MonitoringScope? parent = null);

        void BeginScope(ref Packet packet, string scope);

        void EndScope(ref Packet packet);

        void EndScope(MonitoringScope? scope);
        void Tick();
    }
}
