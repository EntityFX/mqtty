namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{
    public interface IFlowPacket : IPacket
    {
        ushort PacketId { get; }
    }
}
