namespace EntityFX.MqttY.Federation;

public sealed record FederationPublication(long Id, int Origin, long Sequence, int Publisher, long Tick, bool Measurement, byte[] Payload);
