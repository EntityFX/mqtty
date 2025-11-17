namespace EntityFX.MqttY.Contracts.Network
{
    public record ResponsePacket(NetworkPacket Packet, long SendTick, long ReceiveTick);
    public record ResponsePacket<TContext>(NetworkPacket<TContext> PacketWithContext, long SendTick, long ReceiveTick) : 
        ResponsePacket(PacketWithContext, SendTick, ReceiveTick);
}
