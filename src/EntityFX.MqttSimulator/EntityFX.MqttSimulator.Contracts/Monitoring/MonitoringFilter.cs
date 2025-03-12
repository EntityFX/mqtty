using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Monitoring
{
    public class MonitoringFilter
    {
        public record Criteria<TType>(TType? From, TType? To);

        public Criteria<DateTimeOffset>? ByDate { get; init; }

        public Criteria<int>? ByTtl { get; init; }

        public string? ByProtocol { get; init; }

        public MonitoringType[]? ByMonitoringType { get; init; }
        public NodeType[]? ByNodeType { get; init; }

        public int Limit { get; init; } = 500;
    }
}