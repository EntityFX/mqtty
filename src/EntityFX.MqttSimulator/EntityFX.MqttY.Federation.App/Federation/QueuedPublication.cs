namespace EntityFX.MqttY.Federation;

public sealed record QueuedPublication(string Topic, byte[] Payload);
