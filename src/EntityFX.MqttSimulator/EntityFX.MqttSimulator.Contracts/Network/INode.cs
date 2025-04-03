using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.Monitoring;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INode : IWithCounters
    {
        public Guid Id { get; }
        
        public int Index { get; }

        string Address { get; }

        string Name { get; }
        
        string? Group { get; set; }

        int? GroupAmount { get; set; }

        public MonitoringScope? Scope { get; set; }

        NodeType NodeType { get; }

        void Refresh();
        void Reset();

    }
}
