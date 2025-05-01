namespace EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets
{
    public class PacketBase : IPacket
    {
        public MqttPacketType Type { get; init; }
    }
}
