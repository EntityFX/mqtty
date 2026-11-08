using System.Text.Json;
using System.Text.Json.Serialization;

namespace EntityFX.MqttY.Results
{
    /// <summary>
    /// One policy for experiment inputs and artifacts: camelCase, indented,
    /// enum names only (integer enum values are rejected).
    /// </summary>
    public static class JsonPolicies
    {
        public static JsonSerializerOptions Result { get; } = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        };
    }
}
