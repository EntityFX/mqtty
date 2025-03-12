using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Utils;

internal class ConsoleMonitoringProvider : MonitoringProviderBase, IMonitoringProvider
{

    public ConsoleMonitoringProvider(IMonitoring monitoring)
        : base(monitoring)
    {

    }
    protected override void WriteScope(MonitoringScope scope)
    {
        Console.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: " +
            $"Begin Scope <{scope?.Id}> (StartTick={scope?.StartTick}, Ticks={scope?.Ticks}): \"{scope?.ScopeLabel}\"");


        if (scope?.Items?.Any() == true)
        {
            foreach (var item in scope.Items)
            {
                if (item.MonitoringItemType == MonitoringItemType.Scope)
                {
                    WriteScope((MonitoringScope)item);
                }
                else
                {
                    WriteItem((MonitoringItem)item);
                }
            }
        }

        Console.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: End Scope <{scope?.Id}>: \"{scope?.ScopeLabel}\"");
    }

    protected override void WriteItem(MonitoringItem item)
    {
        Console.WriteLine(
            $"{new string(' ', (item.Scope?.Level + 1 ?? 0) * 4)}<{item.Date:u}> " +
            $"(Tick={item.Tick}) {(item.Ttl != null ? $"{{Ttl={item.Ttl}}}" : "")}: " +
            $"{{{item.Type}}} {item.SourceType}[\"{item.From}\"] -> {item.DestinationType}[\"{item.To}\"]" +
            $"{(item.PacketSize > 0 ? $", Packet Size = {item.PacketSize}" : "")}" +
            $"{(!string.IsNullOrEmpty(item.Category) ? $", Category = {item.Category}" : "")}.");
    }
}
