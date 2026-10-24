using System.Text.Json;
using EntityFX.MqttY.Engines.Execution;

namespace EntityFX.MqttY.Engines.Common;

/// <summary>Разбирает JSON-опции эксперимента в SimulationRunOptions.</summary>
public static class SimulationOptionsParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static SimulationRunOptions Parse(JsonElement options)
    {
        if (options.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new SimulationRunOptions();
        }

        return options.Deserialize<SimulationRunOptions>(JsonOptions) ?? new SimulationRunOptions();
    }
}
