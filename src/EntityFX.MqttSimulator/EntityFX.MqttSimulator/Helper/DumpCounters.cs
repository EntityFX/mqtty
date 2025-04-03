using EntityFX.MqttY.Contracts.Counters;
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
            if (counter is CounterGroup counterGroup)
            {
                sb.AppendLine($"{indent}{counter.Name}:");
                foreach (var counterItem in counterGroup.Counters)
                {
                    DumpCounter(counterItem, sb, level);
                }
            }
            else
            {
                sb.AppendLine($"{indent}[{counter.Name}={counter.Value}]");
            }
        }
    }
}
