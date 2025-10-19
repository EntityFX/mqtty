using EntityFX.MqttY.Contracts.Network;

public record struct ResponsePacket(NetworkPacket Packet, long SendTick, long ReceiveTick);
