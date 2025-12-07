using System;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Counter;

namespace EntityFX.MqttY.Helper;

public static class CountersHelper
{
    public static Dictionary<string, GenericCounter> GetAllGenericCounters(this CounterGroup counterGroup)
    {
        var counters = new Dictionary<string, GenericCounter>();
        GetGroupGenericCounters(counterGroup.GroupType, counterGroup, counters);
        return counters;
    }

    public static string GetAllGenericCountersAsText(this CounterGroup counterGroup)
    {
        var sb = new StringBuilder();
        GetGroupGenericCounters(counterGroup, sb);
        return sb.ToString();
    }

    private static void GetGroupGenericCounters(CounterGroup counterGroup, StringBuilder sb)
    {
        sb.AppendLine($"{counterGroup.GroupType}:");
        foreach (var counter in counterGroup.Counters)
        {
            if (counter is GenericCounter gc)
            {
                if (gc.Value == 0) continue;

                sb.AppendLine($"{counter.Name} = {gc.Value}");
            }

            if (counter is CounterGroup cg)
            {
                GetGroupGenericCounters(cg, sb);
            }
        }
    }

    private static void GetGroupGenericCounters(string prefix, CounterGroup counterGroup, Dictionary<string, GenericCounter> counters)
    {
        foreach (var counter in counterGroup.Counters)
        {
            if (counter is GenericCounter gc)
            {
                if (gc.Value == 0) continue;

                counters[$"{prefix}.{counter.Name}"] = gc;
            }

            if (counter is CounterGroup cg)
            {
                GetGroupGenericCounters($"{prefix}.{cg.GroupType}", cg, counters);
            }
        }
    }

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