using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{
    internal class SubscribeAckFormatter : Formatter<SubscribeAckPacket>
    {
        public override MqttPacketType PacketType { get { return MqttPacketType.SubscribeAck; } }

        protected override SubscribeAckPacket Read(byte[] bytes)
        {
            ValidateHeaderFlag(bytes, t => t == MqttPacketType.SubscribeAck, 0x00);

            ReadOnlySpan<byte> packet = bytes;
            var remainingLength = Encoding.DecodeRemainingLength(packet, out var remainingLengthBytesLength);

            var packetIdentifierStartIndex = remainingLengthBytesLength + 1;
            var packetIdentifier = packet.Slice(packetIdentifierStartIndex, 2).ToUInt16();

            var headerLength = 1 + remainingLengthBytesLength + 2;
            var returnCodeLength = remainingLength - 2;

            if (returnCodeLength <= 0)
                throw new MqttException("SubscribeAckFormatter_MissingReturnCodes");

            if (headerLength + returnCodeLength > bytes.Length)
                throw new MqttException("SubscribeAckFormatter_InvalidReturnCodes");

            var returnCodes = new SubscribeReturnCode[returnCodeLength];
            for (var i = 0; i < returnCodeLength; i++)
            {
                var value = packet[headerLength + i];
                if (!Enum.IsDefined(typeof(SubscribeReturnCode), value))
                    throw new MqttException("SubscribeAckFormatter_InvalidReturnCodes");

                returnCodes[i] = (SubscribeReturnCode)value;
            }

            return new SubscribeAckPacket(packetIdentifier, returnCodes);
        }

        protected override byte[] Write(SubscribeAckPacket packet)
        {
            if (packet.ReturnCodes == null || packet.ReturnCodes.Length == 0)
                throw new MqttException("SubscribeAckFormatter_MissingReturnCodes");

            var remainingLength = 2 + packet.ReturnCodes.Length;
            var remainingLengthBytes = MqttEncoder.GetRemainingLengthByteCount(remainingLength);
            var bytes = new byte[1 + remainingLengthBytes + remainingLength];
            var destination = bytes.AsSpan();

            var flags = 0x00;
            var type = Convert.ToInt32(MqttPacketType.SubscribeAck) << 4;
            destination[0] = Convert.ToByte(flags | type);
            var written = 1 + MqttEncoder.WriteRemainingLength(
                destination.Slice(1, remainingLengthBytes), remainingLength);

            MqttEncoder.WriteInteger(destination.Slice(written), packet.PacketId);
            written += 2;

            for (var i = 0; i < packet.ReturnCodes.Length; i++)
            {
                destination[written + i] = Convert.ToByte(packet.ReturnCodes[i]);
            }

            return bytes;
        }
    }
}
