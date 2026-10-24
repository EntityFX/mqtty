namespace EntityFX.MqttY.Federation;

public sealed record FederationNetworkSnapshot(string Name, long Queued,
    IReadOnlyDictionary<string, long> Counters);
