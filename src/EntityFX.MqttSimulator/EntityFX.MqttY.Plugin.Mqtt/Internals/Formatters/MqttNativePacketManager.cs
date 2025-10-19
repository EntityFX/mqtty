using EntityFX.MqttY.Plugin.Mqtt.Contracts;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Formatters;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{
    internal class MqttNativePacketManager : IMqttPacketManager
    {
        readonly IDictionary<MqttPacketType, IFormatter> _formatters;

        public MqttNativePacketManager(IMqttTopicEvaluator mqttTopicEvaluator)
        {
            _formatters = new Dictionary<MqttPacketType, IFormatter>() { 
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

        public TPacket? BytesToPacket<TPacket>(byte[] bytes) where TPacket : IPacket
        {
            var packetType = (MqttPacketType)bytes.Byte(0).Bits(4);
            var formatter = default(IFormatter);

            if (!_formatters.TryGetValue(packetType, out formatter))
                throw new MqttException("PacketUnknown");

            var packet = formatter.Format(bytes);

            return (TPacket?)packet;
        }

        public byte[] PacketToBytes<TPacket>(TPacket packet) where TPacket : IPacket
        {
            var formatter = default(IFormatter);

            if (!_formatters.TryGetValue(packet.Type, out formatter))
                throw new MqttException("PacketUnknown");

            var bytes = formatter.Format(packet);

            return bytes;
        }

        public Task<TPacket?> BytesToPacketAsync<TPacket>(byte[] bytes)
            where TPacket : IPacket
        {
            var packetType = (MqttPacketType)bytes.Byte(0).Bits(4);
            var formatter = default(IFormatter);

            if (!_formatters.TryGetValue(packetType, out formatter))
                throw new MqttException("PacketUnknown");

            var packet = formatter.Format(bytes);

            return Task.FromResult((TPacket?)packet);
        }

        public Task<byte[]> PacketToBytesAsync<TPacket>(TPacket packet)
            where TPacket : IPacket
        {
            var formatter = default(IFormatter);

            if (!_formatters.TryGetValue(packet.Type, out formatter))
                throw new MqttException("PacketUnknown");

            var bytes = formatter.Format(packet);

            return Task.FromResult(bytes);
        }
    }
}
