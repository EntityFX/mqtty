namespace EntityFX.MqttY.LoadBalancing;

public sealed record PhaseResult(string Phase, long StartTick, long EndTick, string Status);
