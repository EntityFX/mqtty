using System.Buffers.Binary;
using System.Text;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters
{
    internal static class ByteExtensions
    {
        internal static bool IsSet(this byte @byte, int bit)
        {
            if (bit > 7)
                throw new ArgumentOutOfRangeException(nameof(bit), "InvalidBitPosition");

            return (@byte & 1 << bit) != 0;
        }

        internal static byte Set(this byte @byte, int bit)
        {
            if (bit > 7)
                throw new ArgumentOutOfRangeException(nameof(bit), "InvalidBitPosition");

            return (byte)(@byte | 1 << bit);
        }

        internal static byte Unset(this byte @byte, int bit)
        {
            if (bit > 7)
                throw new ArgumentOutOfRangeException(nameof(bit), "InvalidBitPosition");

            return (byte)(@byte & ~(1 << bit));
        }

        internal static byte Bits(this byte @byte, int count) =>
            (byte)(@byte >> 8 - count);

        internal static byte Bits(this byte @byte, int index, int count)
        {
            if (index < 1 || index > 8)
                throw new ArgumentOutOfRangeException(nameof(index), "InvalidByteIndex");

            if (index > 1)
            {
                var i = 1;

                do
                {
                    @byte = @byte.Unset(8 - i);
                    i++;
                } while (i < index);
            }

            var to = (byte)(@byte << index - 1);
            return (byte)(to >> 8 - count);
        }

        internal static byte Byte(this byte[] bytes, int index) =>
            (uint)index < (uint)bytes.Length ? bytes[index] : (byte)0;

        internal static byte Byte(this ReadOnlySpan<byte> bytes, int index) =>
            (uint)index < (uint)bytes.Length ? bytes[index] : (byte)0;

        internal static byte[] Bytes(this byte[] bytes, int fromIndex)
        {
            if ((uint)fromIndex >= (uint)bytes.Length)
            {
                return Array.Empty<byte>();
            }

            return bytes.AsSpan(fromIndex).ToArray();
        }

        internal static byte[] Bytes(this byte[] bytes, int fromIndex, int count)
        {
            if ((uint)fromIndex >= (uint)bytes.Length || count <= 0)
            {
                return Array.Empty<byte>();
            }

            var available = Math.Min(count, bytes.Length - fromIndex);
            return bytes.AsSpan(fromIndex, available).ToArray();
        }

        internal static string GetString(this byte[] bytes, int index)
        {
            ReadOnlySpan<byte> span = bytes;
            return GetString(span, index);
        }

        internal static string GetString(this ReadOnlySpan<byte> bytes, int index)
        {
            var length = bytes.GetStringLength(index);
            return length == 0
                ? string.Empty
                : Encoding.UTF8.GetString(bytes.Slice(index + MqttProtocolConsts.StringPrefixLength, length));
        }

        internal static string GetString(this byte[] bytes, int index, out int nextIndex)
        {
            ReadOnlySpan<byte> span = bytes;
            return GetString(span, index, out nextIndex);
        }

        internal static string GetString(this ReadOnlySpan<byte> bytes, int index, out int nextIndex)
        {
            var length = bytes.GetStringLength(index);
            nextIndex = index + MqttProtocolConsts.StringPrefixLength + length;

            return length == 0
                ? string.Empty
                : Encoding.UTF8.GetString(bytes.Slice(index + MqttProtocolConsts.StringPrefixLength, length));
        }

        internal static ushort ToUInt16(this byte[] bytes) =>
            bytes.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : (ushort)0;

        internal static ushort ToUInt16(this ReadOnlySpan<byte> bytes) =>
            bytes.Length >= 2 ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : (ushort)0;

        private static ushort GetStringLength(this ReadOnlySpan<byte> bytes, int index) =>
            bytes.Slice(index, MqttProtocolConsts.StringPrefixLength).ToUInt16();
    }
}
