using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Contracts.Formatters
{
    public interface IMqttPacketManager

    {
        Task<TPacket?> BytesToPacket<TPacket>(byte[] bytes)
                    where TPacket : IPacket;

        Task<byte[]> PacketToBytes<TPacket>(TPacket packet)
                    where TPacket : IPacket;
    }


}
