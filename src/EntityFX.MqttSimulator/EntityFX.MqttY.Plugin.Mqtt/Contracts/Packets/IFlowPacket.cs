namespace EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets
{
    public interface IFlowPacket : IPacket
    {
        ushort PacketId { get; }
    }
}
