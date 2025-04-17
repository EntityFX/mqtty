﻿using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using System.Net.Sockets;

namespace EntityFX.MqttY.Mqtt.Internals.Formatters
{
    internal class MqttNativePacketManager : IMqttPacketManager
    {
        readonly IDictionary<MqttPacketType, IFormatter> formatters;

        public MqttNativePacketManager(IMqttTopicEvaluator mqttTopicEvaluator)
        {
            formatters = new Dictionary<MqttPacketType, IFormatter>() { 
                [MqttPacketType.Connect] = new ConnectFormatter(),
                [MqttPacketType.ConnectAck] = new ConnectAckFormatter(), 
                [MqttPacketType.Subscribe] = new SubscribeFormatter(mqttTopicEvaluator), 
                [MqttPacketType.SubscribeAck] = new SubscribeAckFormatter(), 
                [MqttPacketType.Publish] = new PublishFormatter(mqttTopicEvaluator),
                [MqttPacketType.PublishAck] = new FlowPacketFormatter<PublishAckPacket>(
                    MqttPacketType.PublishAck, id => new PublishAckPacket(id)),
                [MqttPacketType.PublishReceived] = new FlowPacketFormatter<PublishReceivedPacket>(
                    MqttPacketType.PublishReceived, id => new PublishReceivedPacket(id)),
                [MqttPacketType.PublishComplete] = new FlowPacketFormatter<PublishCompletePacket>(
                    MqttPacketType.PublishComplete, id => new PublishCompletePacket(id)),
            };
        }

        public Task<TPacket?> BytesToPacket<TPacket>(byte[] bytes)
            where TPacket : IPacket
        {
            var packetType = (MqttPacketType)bytes.Byte(0).Bits(4);
            var formatter = default(IFormatter);

            if (!formatters.TryGetValue(packetType, out formatter))
                throw new MqttException("PacketUnknown");

            var packet = formatter.Format(bytes);

            return Task.FromResult((TPacket?)packet);
        }

        public Task<byte[]> PacketToBytes<TPacket>(TPacket packet)
            where TPacket : IPacket
        {
            var formatter = default(IFormatter);

            if (!formatters.TryGetValue(packet.Type, out formatter))
                throw new MqttException("PacketUnknown");

            var bytes = formatter.Format(packet);

            return Task.FromResult(bytes);
        }
    }



}
