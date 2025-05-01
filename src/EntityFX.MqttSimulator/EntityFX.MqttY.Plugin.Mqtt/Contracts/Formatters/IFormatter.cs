using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Contracts.Formatters
{
    public interface IFormatter
    {
        MqttPacketType PacketType { get; }

        IPacket Format(byte[] bytes);

        byte[] Format(IPacket packet);
    }


}
