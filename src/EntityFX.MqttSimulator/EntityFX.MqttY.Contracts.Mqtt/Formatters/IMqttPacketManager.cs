using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Contracts.Mqtt.Formatters
{
    public interface IMqttPacketManager

    {
        Task<TPacket?> BytesToPacketAsync<TPacket>(byte[] bytes)
                    where TPacket : IPacket;

        Task<byte[]> PacketToBytesAsync<TPacket>(TPacket packet)
                    where TPacket : IPacket;

        TPacket? BytesToPacket<TPacket>(byte[] bytes)
            where TPacket : IPacket;

        byte[] PacketToBytes<TPacket>(TPacket packet)
                    where TPacket : IPacket;
    }


}
