using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Contracts.Mqtt.Formatters
{
    public interface IFormatter
    {
        MqttPacketType PacketType { get; }

        IPacket Format(byte[] bytes);

        byte[] Format(IPacket packet);
    }


}
