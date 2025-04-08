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

                counterVal = DumpCountersValues(counterGroup.Counters);
                if (!string.IsNullOrEmpty(counterVal))
                {
                    sb.AppendLine($"{indentn}{counterVal}");
                }

                if (counterGroup.Value == 0)
                {
                    foreach (var counterItem in counterGroup.Counters)
                    {
                        DumpCounter(counterItem, sb, level);
                    }
                }
            }
            //else
            //{
            //    counterVal = DumpCountersValues(new[] { counter });
            //    if (!string.IsNullOrEmpty(counterVal))
            //    {
            //        sb.AppendLine($"{indentn}{counterVal}");
            //    }
            //}
        }

        private static string DumpCountersValues(ICounter[] counters)
        {
            var sb = new StringBuilder();
            if (counters.Length > 1)
            {
                foreach (var counterItem in counters)
                {
                    if (counterItem.Value == 0)
                    {
                        continue;
                    }
                    sb.Append($"{counterItem.Name}={counterItem.Value}, ");
                }
            }
            else if (counters.Length == 1)
            {
                if (counters[0].Value == 0)
                {
                    return string.Empty;
                }
                sb.Append($"{counters[0].Name}={counters[0].Value}");
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
