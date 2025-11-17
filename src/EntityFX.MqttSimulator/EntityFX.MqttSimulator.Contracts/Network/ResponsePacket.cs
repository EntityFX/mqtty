namespace EntityFX.MqttY.Contracts.Network
{
    public record struct ResponsePacket(NetworkPacket Packet, long SendTick, long ReceiveTick);
    public record struct ResponsePacket<TData>(NetworkPacket Packet, long SendTick, long ReceiveTick, TData Data);
}
