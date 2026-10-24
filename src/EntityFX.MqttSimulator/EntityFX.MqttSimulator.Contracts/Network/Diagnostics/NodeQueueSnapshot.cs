namespace EntityFX.MqttY.Contracts.Network.Diagnostics;

public sealed record NodeQueueSnapshot(int Incoming, int Outgoing, int GenericResponseWaiters);
