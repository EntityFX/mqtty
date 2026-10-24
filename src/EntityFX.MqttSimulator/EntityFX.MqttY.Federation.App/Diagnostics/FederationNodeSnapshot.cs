using EntityFX.MqttY.Contracts.Mqtt.Diagnostics;
using EntityFX.MqttY.Contracts.Network.Diagnostics;

namespace EntityFX.MqttY.Federation;

public sealed record FederationNodeSnapshot(string Name, bool IsQuiescent,
    NodeQueueSnapshot Queues, MqttPendingSnapshot? Mqtt);
