using EntityFX.MqttY.Contracts.Mqtt;
namespace EntityFX.MqttY.Federation;

public sealed record SourcePublishResult(int Publisher, int Site, int Broker, BrokerQosMetricsSnapshot Metrics);
