namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{
    public interface IPacket
    {
        MqttPacketType Type { get; }
    }
}
