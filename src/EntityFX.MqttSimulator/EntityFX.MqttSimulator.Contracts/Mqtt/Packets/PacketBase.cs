namespace EntityFX.MqttY.Contracts.Mqtt.Packets
{
    public class PacketBase : IPacket
    {
        public MqttPacketType Type { get; init; }
    }
}
