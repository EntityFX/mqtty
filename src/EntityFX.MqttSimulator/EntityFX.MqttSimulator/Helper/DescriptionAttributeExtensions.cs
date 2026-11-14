namespace EntityFX.MqttY.Helper
{
    using System.Collections.Concurrent;
    using System.ComponentModel;

    public static class DescriptionAttributeExtensions
    {
        // Every client and broker builds its own counter group, and each group
        // asks for the description/category of every MqttPacketType value. With
        // ~600 clients that is ~100k metadata lookups, and Enum.GetMember is not
        // cached by the runtime, so it dominated topology construction. The
        // attribute values are immutable, so memoize them per (enum type, name).
        private static readonly ConcurrentDictionary<(Type Type, string Name), string> Descriptions = new();
        private static readonly ConcurrentDictionary<(Type Type, string Name), string> Categories = new();

        public static string GetEnumDescription(this Enum e)
        {
            var key = (e.GetType(), e.ToString());
            if (Descriptions.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var description = (e.GetType().GetMember(key.Item2)[0]
                .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)[0]
                as DescriptionAttribute)?.Description ?? string.Empty;

            Descriptions[key] = description;
            return description;
        }

        public static string GetEnumCategory(this Enum e)
        {
            var key = (e.GetType(), e.ToString());
            if (Categories.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var category = (e.GetType().GetMember(key.Item2)[0]
                .GetCustomAttributes(typeof(CategoryAttribute), inherit: false)[0]
                as CategoryAttribute)?.Category ?? string.Empty;

            Categories[key] = category;
            return category;
        }
    }


}
