namespace EntityFX.MqttY.Plugin.Mqtt.Diagnostics;

/// <summary>Optional observation of the original payload at the publisher's actual ACK or failure.</summary>
public sealed record MqttPublishOutcome(byte[] Payload, long StartedTick, long EndedTick,
    bool Completed, string? Reason);
