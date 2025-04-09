using EntityFX.MqttY.Contracts.Counters;
using System;
using System.Diagnostics.Metrics;
using System.Text;

namespace EntityFX.MqttY.Helper
{
    public static class DumpCounters
    {
        public static string Dump(this ICounter counter)
        {
            var sb = new StringBuilder();

            DumpCounter(counter, sb, 0);

            return sb.ToString();
        }

        private static void DumpCounter(ICounter counter, StringBuilder sb, int level)
        {
            var indent = new string(' ', level * 4);
            level++;
            var indentn = new string(' ', level * 4);
            var counterVal = string.Empty;
            if (counter is CounterGroup counterGroup)
            {
                sb.AppendLine($"{indent}{counter.Name}:");

                counterVal = DumpCountersValues(counterGroup.Counters.ToArray());
                if (!string.IsNullOrEmpty(counterVal))
                {
                    sb.AppendLine($"{indentn}{counterVal}");
                }

                if (counterGroup.Value?.ToString() == string.Empty)
                {
                    foreach (var counterItem in counterGroup.Counters.ToArray())
                    {
                        DumpCounter(counterItem, sb, level);
                    }
                }
            }
        }

        private static string DumpCountersValues(ICounter[] counters)
        {
            var sb = new StringBuilder();
            if (counters.Length > 1)
            {
                foreach (var counterItem in counters)
                {
                    if (counterItem.Value is long and 0 || string.IsNullOrEmpty(counterItem.Value?.ToString()))
                    {
                        continue;
                    }
                    sb.Append($"{counterItem}, ");
                }
            }
            else if (counters.Length == 1)
            {
                if (counters[0].Value?.ToString() == string.Empty)
                {
                    return string.Empty;
                }
                sb.Append($"{counters[0]}");
            }

            var result = sb.ToString();
            if (string.IsNullOrEmpty(result))
            {
                return result; 
            }

            return $"[{result}]";
        }
    }
}
