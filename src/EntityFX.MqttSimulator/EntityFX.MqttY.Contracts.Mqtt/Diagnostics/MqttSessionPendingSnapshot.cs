namespace EntityFX.MqttY.Contracts.Mqtt.Diagnostics;

public sealed record MqttSessionPendingSnapshot(string SessionId, int PendingMessages,
    int PendingAcknowledgements, int RepeatedPendingPacketIdGroups);
