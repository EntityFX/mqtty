﻿using EntityFX.MqttY.Plugin.Mqtt.Contracts;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

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
            var remainingLengthBytesLength = 0;
            var remainingLength = Encoding.DecodeRemainingLength(bytes, out remainingLengthBytesLength);

            var packetFlags = bytes.Byte(0).Bits(5, 4);

            if (packetFlags.Bits(6, 2) == 0x03)
                throw new MqttException("Formatter_InvalidQualityOfService");

            var qos = (MqttQos)packetFlags.Bits(6, 2);
            var duplicated = packetFlags.IsSet(3);

            if (qos == MqttQos.AtMostOnce && duplicated)
                throw new MqttException("PublishFormatter_InvalidDuplicatedWithQoSZero");

            var retainFlag = packetFlags.IsSet(0);

            var topicStartIndex = 1 + remainingLengthBytesLength;
            var nextIndex = 0;
            var topic = bytes.GetString(topicStartIndex, out nextIndex);

            if (!_topicEvaluator.IsValidTopicName(topic))
            {
                var error = string.Format("PublishFormatter_InvalidTopicName", topic);

                throw new MqttException(error);
            }

            var variableHeaderLength = topic.Length + 2;
            var packetId = default(ushort?);

            if (qos != MqttQos.AtMostOnce)
            {
                packetId = bytes.Bytes(nextIndex, 2).ToUInt16();
                variableHeaderLength += 2;
            }

            var publish = new PublishPacket(topic, qos, retainFlag, duplicated, packetId);

            if (remainingLength > variableHeaderLength)
            {
                var payloadStartIndex = 1 + remainingLengthBytesLength + variableHeaderLength;

                publish!.Payload = bytes.Bytes(payloadStartIndex);
            }

            return publish;
        }

        protected override byte[] Write(PublishPacket packet)
        {
            var bytes = new List<byte>();

            var variableHeader = GetVariableHeader(packet);
            var payloadLength = packet.Payload == null ? 0 : packet.Payload.Length;
            var remainingLength = Encoding.EncodeRemainingLength(variableHeader.Length + payloadLength);
            var fixedHeader = GetFixedHeader(packet, remainingLength);

            bytes.AddRange(fixedHeader);
            bytes.AddRange(variableHeader);

            if (packet.Payload != null)
            {
                bytes.AddRange(packet.Payload);
            }

            return bytes.ToArray();
        }

        byte[] GetFixedHeader(PublishPacket packet, byte[] remainingLength)
        {
            if (packet.QualityOfService == MqttQos.AtMostOnce && packet.Duplicated)
                throw new MqttException("PublishFormatter_InvalidDuplicatedWithQoSZero");

            var fixedHeader = new List<byte>();

            var retain = Convert.ToInt32(packet.Retain);
            var qos = Convert.ToInt32(packet.QualityOfService);
            var duplicated = Convert.ToInt32(packet.Duplicated);

            qos <<= 1;
            duplicated <<= 3;

            var flags = Convert.ToByte(retain | qos | duplicated);
            var type = Convert.ToInt32(MqttPacketType.Publish) << 4;

            var fixedHeaderByte1 = Convert.ToByte(flags | type);

            fixedHeader.Add(fixedHeaderByte1);
            fixedHeader.AddRange(remainingLength);

            return fixedHeader.ToArray();
        }

        byte[] GetVariableHeader(PublishPacket packet)
        {
            if (!_topicEvaluator.IsValidTopicName(packet.Topic))
                throw new MqttException("PublishFormatter_InvalidTopicName");

            if (packet.PacketId.HasValue && packet.QualityOfService == MqttQos.AtMostOnce)
                throw new MqttException("PublishFormatter_InvalidPacketId");

            if (!packet.PacketId.HasValue && packet.QualityOfService != MqttQos.AtMostOnce)
                throw new MqttException("PublishFormatter_PacketIdRequired");

            var variableHeader = new List<byte>();

            var topicBytes = Encoding.EncodeString(packet.Topic);

            variableHeader.AddRange(topicBytes);

            if (packet.PacketId.HasValue)
            {
                var packetIdBytes = Encoding.EncodeInteger(packet.PacketId.Value);

                variableHeader.AddRange(packetIdBytes);
            }

            return variableHeader.ToArray();
        }
    }


}
