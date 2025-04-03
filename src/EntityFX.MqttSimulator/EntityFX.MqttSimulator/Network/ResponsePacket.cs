using EntityFX.MqttY.Contracts.Network;

public record ResponsePacket(NetworkPacket Packet, long SendTick, long ReceiveTick);
