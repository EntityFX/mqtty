namespace EntityFX.MqttY.Federation;

public sealed record FederatorTargetSnapshot(int Site, int Target, int Queued,
    string OutputClient, bool HasPendingPublish);
