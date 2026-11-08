using EntityFX.MqttY.Contracts.Mqtt;
using System.Buffers.Binary;
using System.Text;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{
    internal class MqttEncoder
    {
        internal static MqttEncoder Default { get; } = new MqttEncoder();

        internal byte[] EncodeString(string text)
        {
            var length = GetStringByteCount(text);
            var bytes = new byte[MqttProtocolConsts.StringPrefixLength + length];
            WriteString(bytes, text);
            return bytes;
        }

        internal int GetStringByteCount(string? text)
        {
            var length = Encoding.UTF8.GetByteCount(text ?? string.Empty);
            if (length > MqttProtocolConsts.MaxIntegerLength)
            {
                throw new MqttException("StringMaxLengthExceeded");
            }

            return length;
        }

        internal void WriteString(Span<byte> destination, string? text)
        {
            var written = Encoding.UTF8.GetBytes(text ?? string.Empty,
                destination.Slice(MqttProtocolConsts.StringPrefixLength));

            if (written > MqttProtocolConsts.MaxIntegerLength)
            {
                throw new MqttException("StringMaxLengthExceeded");
            }

            BinaryPrimitives.WriteUInt16BigEndian(destination, checked((ushort)written));
        }

        internal byte[] EncodeInteger(int number)
        {
            if (number > MqttProtocolConsts.MaxIntegerLength)
            {
                throw new MqttException("IntegerMaxValueExceeded");
            }

            return EncodeInteger((ushort)number);
        }

        internal byte[] EncodeInteger(ushort number)
        {
            var bytes = new byte[2];
            WriteInteger(bytes, number);
            return bytes;
        }

        internal static void WriteInteger(Span<byte> destination, ushort number) =>
            BinaryPrimitives.WriteUInt16BigEndian(destination, number);

        internal byte[] EncodeRemainingLength(int length)
        {
            var count = GetRemainingLengthByteCount(length);
            var bytes = new byte[count];
            WriteRemainingLength(bytes, length);
            return bytes;
        }

        internal static int GetRemainingLengthByteCount(int length)
        {
            if (length < 0)
            {
                throw new MqttException("MalformedRemainingLength");
            }

            return length switch
            {
                < 128 => 1,
                < 16_384 => 2,
                < 2_097_152 => 3,
                <= 268_435_455 => 4,
                _ => throw new MqttException("MalformedRemainingLength")
            };
        }

        internal static int WriteRemainingLength(Span<byte> destination, int length)
        {
            _ = GetRemainingLengthByteCount(length);
            var offset = 0;

            do
            {
                var encoded = length % 128;
                length /= 128;

                if (length > 0)
                {
                    encoded |= 128;
                }

                destination[offset++] = (byte)encoded;
            } while (length > 0);

            return offset;
        }

        internal int DecodeRemainingLength(byte[] packet, out int arrayLength) =>
            DecodeRemainingLength(packet.AsSpan(), out arrayLength);

        internal int DecodeRemainingLength(ReadOnlySpan<byte> packet, out int arrayLength)
        {
            var multiplier = 1;
            var value = 0;
            var index = 0;
            byte encodedByte;

            do
            {
                index++;
                encodedByte = packet[index];
                value += (encodedByte & 127) * multiplier;

                if (multiplier > 128 * 128 * 128 || index > 4)
                    throw new MqttException("MalformedRemainingLength");

                multiplier *= 128;
            } while ((encodedByte & 128) != 0);

            arrayLength = index;
            return value;
        }

        internal int DecodeRemainingLength(byte[] packet, out int arrayLength, out byte[] bytes)
        {
            var value = DecodeRemainingLength(packet.AsSpan(), out arrayLength);
            bytes = packet.AsSpan(1, arrayLength).ToArray();
            return value;
        }
    }
}
