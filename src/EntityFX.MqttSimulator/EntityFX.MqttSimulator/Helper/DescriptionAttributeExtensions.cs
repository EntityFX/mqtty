namespace EntityFX.MqttY.Helper
{
    using System.ComponentModel;

    public static class DescriptionAttributeExtensions
    {
        public static string GetEnumDescription(this Enum e)
        {
            var descriptionAttribute = e.GetType().GetMember(e.ToString())[0]
                .GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)[0]
                as DescriptionAttribute;

            return descriptionAttribute?.Description ?? string.Empty;
        }

        public static string GetEnumCategory(this Enum e)
        {
            var descriptionAttribute = e.GetType().GetMember(e.ToString())[0]
                .GetCustomAttributes(typeof(CategoryAttribute), inherit: false)[0]
                as CategoryAttribute;

            return descriptionAttribute?.Category ?? string.Empty;
        }
    }


}
