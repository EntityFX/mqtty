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
        if (scope?.Items?.Any() != true)
        {
            return;
        }

        Console.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: " +
        $"Begin Scope <{scope?.Id}> (StartTick={scope?.StartTick}, Ticks={scope?.Ticks}): \"{scope?.ScopeLabel}\"");



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


        Console.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: End Scope <{scope?.Id}>: \"{scope?.ScopeLabel}\"");
    }

    protected override void WriteItem(MonitoringItem item)
    {
        Console.WriteLine(GetMonitoringLine(item));
    }
}
