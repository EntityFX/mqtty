using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Contracts.Mqtt.Formatters
{
    public interface IMqttPacketManager

    {
        Task<TPacket?> BytesToPacket<TPacket>(byte[] bytes)
                    where TPacket : IPacket;

        Task<byte[]> PacketToBytes<TPacket>(TPacket packet)
                    where TPacket : IPacket;
    }


}
