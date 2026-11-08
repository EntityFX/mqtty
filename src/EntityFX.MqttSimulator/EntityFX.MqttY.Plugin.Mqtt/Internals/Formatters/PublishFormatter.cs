using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{
    internal class PublishFormatter : Formatter<PublishPacket>
    {
        readonly IMqttTopicEvaluator _topicEvaluator;

        public PublishFormatter(IMqttTopicEvaluator topicEvaluator)
        {
            this._topicEvaluator = topicEvaluator;
        }

        public override MqttPacketType PacketType { get { return MqttPacketType.Publish; } }

        protected override PublishPacket Read(byte[] bytes)
        {
            ReadOnlySpan<byte> packet = bytes;
            var remainingLength = Encoding.DecodeRemainingLength(packet, out var remainingLengthBytesLength);

            var packetFlags = packet.Byte(0).Bits(5, 4);

            if (packetFlags.Bits(6, 2) == 0x03)
                throw new MqttException("Formatter_InvalidQualityOfService");

            var qos = (MqttQos)packetFlags.Bits(6, 2);
            var duplicated = packetFlags.IsSet(3);

            if (qos == MqttQos.AtMostOnce && duplicated)
                throw new MqttException("PublishFormatter_InvalidDuplicatedWithQoSZero");

            var retainFlag = packetFlags.IsSet(0);

            var topicStartIndex = 1 + remainingLengthBytesLength;
            var topic = packet.GetString(topicStartIndex, out var nextIndex);

            if (!_topicEvaluator.IsValidTopicName(topic))
            {
                var error = string.Format("PublishFormatter_InvalidTopicName", topic);

                throw new MqttException(error);
            }

            var variableHeaderLength = System.Text.Encoding.UTF8.GetByteCount(topic) +
                MqttProtocolConsts.StringPrefixLength;
            var packetId = default(ushort?);

            if (qos != MqttQos.AtMostOnce)
            {
                packetId = packet.Slice(nextIndex, 2).ToUInt16();
                variableHeaderLength += 2;
            }

            var publish = new PublishPacket(topic, qos, retainFlag, duplicated, packetId);

            if (remainingLength > variableHeaderLength)
            {
                var payloadStartIndex = 1 + remainingLengthBytesLength + variableHeaderLength;

                publish!.Payload = packet.Slice(payloadStartIndex).ToArray();
            }

            return publish;
        }

        protected override byte[] Write(PublishPacket packet)
        {
            Validate(packet);

            var topicLength = Encoding.GetStringByteCount(packet.Topic);
            var packetIdLength = packet.PacketId.HasValue ? 2 : 0;
            var payloadLength = packet.Payload?.Length ?? 0;
            var variableHeaderLength = topicLength + MqttProtocolConsts.StringPrefixLength + packetIdLength;
            var remainingLength = variableHeaderLength + payloadLength;
            var remainingLengthBytes = MqttEncoder.GetRemainingLengthByteCount(remainingLength);

            var bytes = new byte[1 + remainingLengthBytes + remainingLength];
            var destination = bytes.AsSpan();

            destination[0] = GetFixedHeaderByte(packet);
            var written = 1 + MqttEncoder.WriteRemainingLength(
                destination.Slice(1, remainingLengthBytes), remainingLength);
            written += MqttProtocolConsts.StringPrefixLength + topicLength + packetIdLength;
            Encoding.WriteString(destination.Slice(1 + remainingLengthBytes), packet.Topic);

            if (packet.PacketId.HasValue)
            {
                MqttEncoder.WriteInteger(
                    destination.Slice(1 + remainingLengthBytes + MqttProtocolConsts.StringPrefixLength + topicLength, 2),
                    packet.PacketId.Value);
            }

            if (payloadLength != 0)
            {
                packet.Payload!.CopyTo(destination.Slice(written));
            }

            return bytes;
        }

        private void Validate(PublishPacket packet)
        {
            if (!_topicEvaluator.IsValidTopicName(packet.Topic))
                throw new MqttException("PublishFormatter_InvalidTopicName");

            if (packet.PacketId.HasValue && packet.QualityOfService == MqttQos.AtMostOnce)
                throw new MqttException("PublishFormatter_InvalidPacketId");

            if (!packet.PacketId.HasValue && packet.QualityOfService != MqttQos.AtMostOnce)
                throw new MqttException("PublishFormatter_PacketIdRequired");

            if (packet.QualityOfService == MqttQos.AtMostOnce && packet.Duplicated)
                throw new MqttException("PublishFormatter_InvalidDuplicatedWithQoSZero");
        }

        private static byte GetFixedHeaderByte(PublishPacket packet)
        {
            var retain = Convert.ToInt32(packet.Retain);
            var qos = Convert.ToInt32(packet.QualityOfService);
            var duplicated = Convert.ToInt32(packet.Duplicated);

            qos <<= 1;
            duplicated <<= 3;

            var flags = Convert.ToByte(retain | qos | duplicated);
            var type = Convert.ToInt32(MqttPacketType.Publish) << 4;

            return Convert.ToByte(flags | type);
        }
    }
}
