using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{
    public interface IMonitoring
    {
        public event EventHandler<MonitoringItem> Added;

        public IEnumerable<MonitoringItem> Items { get; }

        void Push(string from, NodeType fromType, string to, NodeType toType, byte[]? packet, MonitoringType type, object details);

        void Push(INode from, INode to, byte[]? packet, MonitoringType type, object details);
    }
}
