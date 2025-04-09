using EntityFX.MqttY.Contracts.NetworkLogger;

internal class ConsoleNetworkLoggerProvider : NetworkLoggerBase, IINetworkLoggerProvider
{

    public ConsoleNetworkLoggerProvider(INetworkLogger monitoring)
        : base(monitoring)
    {

    }

    protected override void WriteScope(NetworkLoggerScope scope)
    {
        if (scope?.Items?.Any() != true)
        {
            return;
        }

        Console.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope?.Date:u}>: " +
        $"Begin Scope <{scope?.Id}> (StartTick={scope?.StartTick}, Ticks={scope?.Ticks}): \"{scope?.ScopeLabel}\"");

        if (scope?.Items?.Any() == true)
        {
            foreach (var item in scope.Items)
            {
                if (item.ItemType == NetworkLoggerItemType.Scope)
                {
                    WriteScope((NetworkLoggerScope)item);
                }
                else
                {
                    WriteItem((NetworkLoggerItem)item);
                }
            }
        }

        Console.WriteLine(
            $"{new string(' ', (scope?.Level ?? 0) * 4)}" +
            $"<{scope?.Date:u}>: End Scope <{scope?.Id}>: \"{scope?.ScopeLabel}\"");
    }

    protected override void WriteItem(NetworkLoggerItem item)
    {
        Console.WriteLine(GetMonitoringLine(item));
    }
}
