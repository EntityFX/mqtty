using EntityFX.MqttY.Plugin.Mqtt.Contracts;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{
    internal class SubscribeAckFormatter : Formatter<SubscribeAckPacket>
    {
        public override MqttPacketType PacketType { get { return MqttPacketType.SubscribeAck; } }

        protected override SubscribeAckPacket Read(byte[] bytes)
        {
            ValidateHeaderFlag(bytes, t => t == MqttPacketType.SubscribeAck, 0x00);

            var remainingLengthBytesLength = 0;
            var remainingLength = Encoding.DecodeRemainingLength(bytes, out remainingLengthBytesLength);

            var packetIdentifierStartIndex = remainingLengthBytesLength + 1;
            var packetIdentifier = bytes.Bytes(packetIdentifierStartIndex, 2).ToUInt16();

            var headerLength = 1 + remainingLengthBytesLength + 2;
            var returnCodeBytes = bytes.Bytes(headerLength);

            if (!returnCodeBytes.Any())
                throw new MqttException("SubscribeAckFormatter_MissingReturnCodes");

            if (returnCodeBytes.Any(b => !Enum.IsDefined(typeof(SubscribeReturnCode), b)))
                throw new MqttException("SubscribeAckFormatter_InvalidReturnCodes");

            var returnCodes = returnCodeBytes.Select(b => (SubscribeReturnCode)b).ToArray();

            return new SubscribeAckPacket(packetIdentifier, returnCodes);
        }

        protected override byte[] Write(SubscribeAckPacket packet)
        {
            var bytes = new List<byte>();

            var variableHeader = GetVariableHeader(packet);
            var payload = GetPayload(packet);
            var remainingLength = Encoding.EncodeRemainingLength(variableHeader.Length + payload.Length);
            var fixedHeader = GetFixedHeader(remainingLength);

            bytes.AddRange(fixedHeader);
            bytes.AddRange(variableHeader);
            bytes.AddRange(payload);

            return bytes.ToArray();
        }

        byte[] GetFixedHeader(byte[] remainingLength)
        {
            var fixedHeader = new List<byte>();

            var flags = 0x00;
            var type = Convert.ToInt32(MqttPacketType.SubscribeAck) << 4;

            var fixedHeaderByte1 = Convert.ToByte(flags | type);

            fixedHeader.Add(fixedHeaderByte1);
            fixedHeader.AddRange(remainingLength);

            return fixedHeader.ToArray();
        }

        byte[] GetVariableHeader(SubscribeAckPacket packet)
        {
            var variableHeader = new List<byte>();

            var packetIdBytes = Encoding.EncodeInteger(packet.PacketId);

            variableHeader.AddRange(packetIdBytes);

            return variableHeader.ToArray();
        }

        byte[] GetPayload(SubscribeAckPacket packet)
        {
            if (packet.ReturnCodes == null || !packet.ReturnCodes.Any())
                throw new MqttException("SubscribeAckFormatter_MissingReturnCodes");

            return packet.ReturnCodes
                .Select(c => Convert.ToByte(c))
                .ToArray();
        }
    }


}
