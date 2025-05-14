namespace EntityFX.MqttY.Helper
{
    public static class BytesHumanConverter
    {
        public enum Units
        {
            K = 1, M, G, T, P, E, Z, Y
        }

        public static string ToHumanBiBytes(this double bytes, string? unitOfMeasure = "iB")
        {
            return Normalize(bytes, unitOfMeasure);
        }

        public static string ToHumanBytes(this double bytes, string? unitOfMeasure = "B")
        {
            return Normalize(bytes, unitOfMeasure, 1000);
        }

        public static string ToHumanBits(this double bytes, string? unitOfMeasure = "b")
        {
            return Normalize(bytes * 8, unitOfMeasure, 1000);
        }

        public static string ToHumanBiBits(this double bytes, string? unitOfMeasure = "bibit")
        {
            return Normalize(bytes * 8, unitOfMeasure);
        }


        public static string Normalize(this double unit, string? measureUnit, int @base = 1 << 10)
        {
            var index = 0;

            if (double.IsInfinity(unit)) {
                return $"Inf {(index > 0 ? (Units)index : string.Empty)}{measureUnit}";
            }

            var normalized = unit;
            while (normalized > @base)
            {
                index++;
                normalized /= @base;
            }

            return $"{Math.Round(normalized, 2).ToString("##.##")} {(index > 0 ? (Units)index : string.Empty)}{measureUnit}";
        }
    }
}
