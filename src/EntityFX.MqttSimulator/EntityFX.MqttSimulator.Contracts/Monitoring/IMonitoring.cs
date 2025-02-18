using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{
    public interface IMonitoring
    {
        public event EventHandler<MonitoringItem> Added;

        public event EventHandler<MonitoringScope> ScopeStarted;

        public event EventHandler<MonitoringScope> ScopeEnded;

        public IEnumerable<MonitoringItem> Items { get; }

        public void Push(Packet packet, MonitoringType type, string? category);

        void Push(INode from, INode to, byte[]? packet, MonitoringType type, string? category, Guid? scopeId = null);

        MonitoringScope BeginScope(string scopeMessage);

        MonitoringScope TryBeginScope(ref Packet packet, string scope);

        MonitoringScope? TryEndScope(ref Packet packet);

        MonitoringScope? EndScope(Guid? scopeId);
    }
}
