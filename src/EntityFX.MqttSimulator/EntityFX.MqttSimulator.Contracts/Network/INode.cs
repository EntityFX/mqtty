using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.NetworkLogger;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INode : IWithCounters
    {
        Guid Id { get; }
        
        int Index { get; }

        string Address { get; }

        string Name { get; }
        
        string? Group { get; set; }

        int? GroupAmount { get; set; }

        NetworkLoggerScope? Scope { get; set; }

        NodeType NodeType { get; }

        void Refresh();

        void Reset();

        void Clear();

        INetworkSimulator? NetworkSimulator { get; }

    }
}
