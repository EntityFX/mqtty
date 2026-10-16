namespace EntityFX.MqttY.Contracts.Mqtt;

/// <summary>Publisher-side identity and clock origin, carried outside the MQTT wire payload.</summary>
public sealed record PublishIdentity(long Sequence, long StartedTick);
