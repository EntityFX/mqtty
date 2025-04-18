using System.Collections;

namespace EntityFX.MqttY.Helper
{
    public static class FormatExtensions
    {

        public static string CenterString(this string stringToCenter, int totalLength)
        {
            return stringToCenter.PadLeft(((totalLength - stringToCenter.Length) / 2)
                                + stringToCenter.Length)
                       .PadRight(totalLength).Substring(0, totalLength);
        }

        public static string CenterString(this char stringToCenter, int totalLength)
        {
            return stringToCenter.ToString().CenterString(totalLength);
        }

        public static string BitsToStringPad(this BitArray bitArray, int countBits, int padLen)
        {
            return string.Concat(bitArray.OfType<bool>()
                .Take(countBits).Reverse().Select(b => (b ? 1 : 0).ToString().CenterString(padLen)));
        }

        public static char FlagToStringRep(this byte flag, bool ommit)
        {
            if (ommit)
            {
                return 'x';
            }

            return flag > 0 ? '1' : '0';
        }

        public static string ToBytesHexString(this byte[] bytes)
        {
            return string.Join(' ', bytes.Select(b => $"0x{b:X2}"));
        }
    }
}
