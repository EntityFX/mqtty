﻿using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{
    internal class FlowPacketFormatter<T> : Formatter<T>
        where T : class, IFlowPacket
    {
        readonly MqttPacketType _packetType;
        readonly Func<ushort, T> _packetFactory;

        public FlowPacketFormatter(MqttPacketType packetType, Func<ushort, T> packetFactory)
        {
            this._packetType = packetType;
            this._packetFactory = packetFactory;
        }

        public override MqttPacketType PacketType { get { return _packetType; } }

        protected override T Read(byte[] bytes)
        {
            ValidateHeaderFlag(bytes, t => t == MqttPacketType.PublishRelease, 0x02);
            ValidateHeaderFlag(bytes, t => t != MqttPacketType.PublishRelease, 0x00);

            var remainingLengthBytesLength = 0;

            Encoding.DecodeRemainingLength(bytes, out remainingLengthBytesLength);

            var packetIdIndex = MqttProtocolConsts.PacketTypeLength + remainingLengthBytesLength;
            var packetIdBytes = bytes.Bytes(packetIdIndex, 2);

            return _packetFactory(packetIdBytes.ToUInt16());
        }

        protected override byte[] Write(T packet)
        {
            var variableHeader = Encoding.EncodeInteger(packet.PacketId);
            var remainingLength = Encoding.EncodeRemainingLength(variableHeader.Length);
            var fixedHeader = GetFixedHeader(packet.Type, remainingLength);
            var bytes = new byte[fixedHeader.Length + variableHeader.Length];

            fixedHeader.CopyTo(bytes, 0);
            variableHeader.CopyTo(bytes, fixedHeader.Length);

            return bytes;
        }

        byte[] GetFixedHeader(MqttPacketType packetType, byte[] remainingLength)
        {
            // MQTT 2.2.2: http://docs.oasis-open.org/mqtt/mqtt/v3.1.1/csprd02/mqtt-v3.1.1-csprd02.html#_Toc385349758
            // The flags for PUBREL are different than for the other flow packets.
            var flags = packetType == MqttPacketType.PublishRelease ? 0x02 : 0x00;
            var type = Convert.ToInt32(packetType) << 4;
            var fixedHeaderByte1 = Convert.ToByte(flags | type);
            var fixedHeader = new byte[1 + remainingLength.Length];

            fixedHeader[0] = fixedHeaderByte1;
            remainingLength.CopyTo(fixedHeader, 1);

            return fixedHeader;
        }
    }



}
