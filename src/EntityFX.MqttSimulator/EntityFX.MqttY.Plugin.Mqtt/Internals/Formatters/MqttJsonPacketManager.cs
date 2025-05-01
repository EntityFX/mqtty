﻿using System.Text;
using System.Text.Json;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Formatters;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{
    internal class MqttJsonPacketManager : IMqttPacketManager

    {
        public async Task<byte[]> PacketToBytes<TPacket>(TPacket packet)
            where TPacket : IPacket
        {
            using var stream = new MemoryStream();
            await JsonSerializer.SerializeAsync(stream, packet);
            stream.Position = 0;
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            return Encoding.UTF8.GetBytes(json);
        }

        public async Task<TPacket?> BytesToPacket<TPacket>(byte[] bytes)
            where TPacket : IPacket
        {
            using var stream = new MemoryStream(bytes);
            var packet = await JsonSerializer.DeserializeAsync<TPacket>(stream);
            return packet;
        }
    }
}
