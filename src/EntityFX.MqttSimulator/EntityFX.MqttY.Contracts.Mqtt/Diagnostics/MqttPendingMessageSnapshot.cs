namespace EntityFX.MqttY.Contracts.Mqtt.Diagnostics;

/// <summary>Created tick is unavailable in the existing state; no age is inferred.</summary>
public sealed record MqttPendingMessageSnapshot(string SessionId, string Status, int Qos,
    ushort? PacketId, long? CorrelationId, string Topic, int PayloadBytes, string PayloadSha256);
