using System.Text;
using System.Text.Json;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Scenarios;

namespace EntityFX.MqttY.Plugin.Scenarios;

public class SaveNetworkCountersJsonAction : ScenarioAction<NetworkSimulation, PathOptions>
{
    public override async Task ExecuteAsync()
    {
        if (Config == null)
        {
            throw new ArgumentNullException(nameof(Config));
        }

        var path = ReplaceParams(Config.Path, Scenario!.Name!);
        var fileName = ReplaceParams(Config.File, Scenario.Name!);

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        var fullPath = Path.Combine(path, fileName);

        var counters = new Dictionary<string, object>();

        VisitCounter(Context!.NetworkGraph!.Counters!, counters);

        var countersJson = JsonSerializer.Serialize(counters, new JsonSerializerOptions() { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });

        await File.WriteAllTextAsync(fullPath, countersJson, Encoding.UTF8);
    }

    private string ReplaceParams(string source, string scenario)
    {
        return source
            .Replace("{scenario}", scenario)
            .Replace("{date}", $"{DateTime.Now:yyyy_MM_dd__HH_mm}");
    }

    private static void VisitCounter(ICounter counter, Dictionary<string, object> counters)
    {
        if (counter is CounterGroup counterGroup)
        {
            var subCounters = new Dictionary<string, object>();
            foreach (var counterItem in counterGroup.Counters.ToArray())
            {
                VisitCounter(counterItem, subCounters);
            }

            counters[counter.Name] = subCounters;
            counters[counter.Name] = subCounters;
        }
        else
        {
            counters[counter.Name] = counter.Value;
            counters[$"{counter.Name}__Avg"] = counter.Average();
        }
    }
}