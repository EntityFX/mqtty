namespace EntityFX.MqttY.Contracts.Mqtt.Diagnostics;

public sealed record MqttPendingAcknowledgementSnapshot(string SessionId, string Type, ushort PacketId);
