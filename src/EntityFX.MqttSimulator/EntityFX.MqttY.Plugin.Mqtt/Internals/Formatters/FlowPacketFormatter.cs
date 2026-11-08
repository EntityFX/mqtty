using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;

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

            ReadOnlySpan<byte> packet = bytes;
            var remainingLength = Encoding.DecodeRemainingLength(packet, out var remainingLengthBytesLength);

            if (remainingLength < 2)
                throw new MqttException("InvalidPacketLength");

            var packetIdIndex = MqttProtocolConsts.PacketTypeLength + remainingLengthBytesLength;
            return _packetFactory(packet.Slice(packetIdIndex, 2).ToUInt16());
        }

        protected override byte[] Write(T packet)
        {
            var bytes = new byte[4];
            var fixedHeaderByte1 = GetFixedHeaderByte(packet.Type);
            var remainingLengthBytes = MqttEncoder.WriteRemainingLength(bytes.AsSpan(1), 2);
            var packetIdIndex = 1 + remainingLengthBytes;

            bytes[0] = fixedHeaderByte1;
            MqttEncoder.WriteInteger(bytes.AsSpan(packetIdIndex), packet.PacketId);

            return bytes;
        }

        private static byte GetFixedHeaderByte(MqttPacketType packetType)
        {
            // MQTT 2.2.2: http://docs.oasis-open.org/mqtt/mqtt/v3.1.1/csprd02/mqtt-v3.1.1-csprd02.html#_Toc385349758
            // The flags for PUBREL are different than for the other flow packets.
            var flags = packetType == MqttPacketType.PublishRelease ? 0x02 : 0x00;
            var type = Convert.ToInt32(packetType) << 4;
            return Convert.ToByte(flags | type);
        }
    }
}
