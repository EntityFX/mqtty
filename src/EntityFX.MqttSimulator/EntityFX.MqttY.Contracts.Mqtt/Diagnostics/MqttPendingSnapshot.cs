namespace EntityFX.MqttY.Contracts.Mqtt.Diagnostics;

public sealed record MqttPendingSnapshot(int TotalSessions, int TotalPendingMessages,
    int TotalPendingAcknowledgements, int ProcessingQueueCount,
    IReadOnlyDictionary<string, int> MessageStates, IReadOnlyDictionary<string, int> AcknowledgementTypes,
    MqttSessionPendingSnapshot[] Sessions, MqttPendingMessageSnapshot[] Messages,
    MqttPendingAcknowledgementSnapshot[] Acknowledgements, bool Truncated);
