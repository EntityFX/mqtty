using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Contracts.Mqtt.Formatters
{
    public interface IFormatter
    {
        MqttPacketType PacketType { get; }

        Task<IPacket> FormatAsync(byte[] bytes);

        Task<byte[]> FormatAsync(IPacket packet);
    }


}
