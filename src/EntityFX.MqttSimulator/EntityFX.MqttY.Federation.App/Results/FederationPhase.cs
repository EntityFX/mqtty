namespace EntityFX.MqttY.Federation;

public sealed record FederationPhase(string Name, long StartTick, long EndTick, string Status);
