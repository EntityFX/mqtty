using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.LoadBalancing;

public sealed record BackendResult(int Backend, int Publishers, BrokerQosMetricsSnapshot Metrics,
    long CompletedBeforeDrain, long UniqueReceived, long UniqueReceivedBeforeDrain);
