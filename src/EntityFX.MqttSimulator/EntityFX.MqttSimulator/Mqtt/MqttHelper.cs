using EntityFX.MqttY.Contracts.Mqtt.Packets;
using System.Text;
using System.Text.Json;

namespace EntityFX.MqttY.Mqtt
{
    internal static class MqttHelper
    {
        public static byte[] PacketToBytes<TPacket>(this TPacket packet)
            where TPacket : IPacket
        {
            var json = JsonSerializer.Serialize(packet);
            return  Encoding.UTF8.GetBytes(json);
        }

        public static TPacket? BytesToPacket<TPacket>(this byte[] bytes)
            where TPacket : IPacket
        {
            var payload = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
            return JsonSerializer.Deserialize<TPacket>(payload);
        }
    }
}
