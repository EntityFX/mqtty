namespace EntityFX.MqttY.Federation;

public sealed record FederationDrainSnapshot(string Phase, string Event, long Tick, bool IsIdle,
    FederationNetworkSnapshot[] Networks, FederationNodeSnapshot[] Nodes,
    FederatorTargetSnapshot[] FederatorTargets);
