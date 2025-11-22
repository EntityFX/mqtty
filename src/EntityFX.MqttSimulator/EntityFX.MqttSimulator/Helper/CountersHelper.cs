using System.Text;
using EntityFX.MqttY.Contracts.Counters;

namespace EntityFX.MqttY.Helper;

public static class CountersHelper
{
    public static string PrintCounters(this ICounter counters)
    {
        var sb = new StringBuilder();
        var level = 0;

        PrintCountersRecursively(counters, level, sb);

        return sb.ToString();
    }

    private static void PrintCountersRecursively(ICounter counters, int level, StringBuilder sb)
    {
        var spc = new string(' ', level * 4);
        if (counters is CounterGroup counterGroup)
        {
            sb.AppendLine($"{spc}[{counterGroup.GroupType}] {counters.Name}:");
            foreach (var counter in counterGroup.Counters)
            {
                PrintCountersRecursively(counter, level + 1, sb);
            }

            if (!counterGroup.Counters.Any())
            {
                sb.AppendLine($"{new string(' ', (level + 1) * 4)} - ");
            }
        }
        else
        {
            sb.AppendLine($"{spc}{counters.Name} = {counters.Value}");
        }
    }
}