namespace EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets
{
    public interface IPacket
    {
        MqttPacketType Type { get; }
    }
}
