using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Utils;

internal class ConsoleMonitoringProvider
{
    private readonly IMonitoring monitoring;
    private readonly PlantUmlGraphGenerator umlGenerator;
    private static object _lock = new object();

    public ConsoleMonitoringProvider(IMonitoring monitoring)
    {
        this.monitoring = monitoring;
        this.umlGenerator = new PlantUmlGraphGenerator();
    }

    public void Start()
    {
        monitoring.Added += (sender, e) =>
            PrintItem(e);

        monitoring.ScopeStarted += (sender, scope) =>
            BeginScope(scope);

        monitoring.ScopeEnded += (sender, scope) =>
        {
            PrintScopeItems(scope);
        };
    }

    private void BeginScope(MonitoringScope scope)
    {
    }

    private void PrintScopeItems(MonitoringScope scope)
    {
        if (scope.Parent != null)
        {
            return;
        }

        lock (_lock)
        {
            PrintScope(scope);
            var sequence = umlGenerator.GenerateSequence(monitoring, scope);
        }
    }

    private void PrintScope(MonitoringScope scope)
    {
        Console.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: " +
            $"Begin Scope <{scope?.Id}> (StartTick={scope?.StartTick}, Ticks={scope?.Ticks}): \"{scope?.ScopeLabel}\"");


        if (scope?.Items?.Any() == true)
        {
            foreach (var item in scope.Items)
            {
                if (item.MonitoringItemType == MonitoringItemType.Scope)
                {
                    PrintScope((MonitoringScope)item);
                }
                else
                {
                    PrintMonitoringItem((MonitoringItem)item);
                }
            }
        }

        Console.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: End Scope <{scope?.Id}>: \"{scope?.ScopeLabel}\"");
    }

    private void PrintItem(MonitoringItem item)
    {
        lock (_lock)
        {
            if (item.Scope != null) return;

            PrintMonitoringItem(item);
        }
    }

    private void PrintMonitoringItem(MonitoringItem item)
    {
        Console.WriteLine(
            $"{new string(' ', (item.Scope?.Level + 1 ?? 0) * 4)}<{item.Date:u}> " +
            $"(Tick={item.Tick}) {(item.Ttl != null ? $"{{Ttl={item.Ttl}}}" : "")}: " +
            $"{{{item.Type}}} {item.SourceType}[\"{item.From}\"] -> {item.DestinationType}[\"{item.To}\"]" +
            $"{(item.PacketSize > 0 ? $", Packet Size = {item.PacketSize}" : "")}" +
            $"{(!string.IsNullOrEmpty(item.Category) ? $", Category = {item.Category}" : "")}.");
    }
}
