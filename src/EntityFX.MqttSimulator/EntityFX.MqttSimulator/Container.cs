using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY;

public static class Container
{
    private static object _lock = new object();

    public static IServiceCollection ConfigureServices(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddScoped<IMonitoring, Monitoring>((sb) =>
            {
                var monitoring = new Monitoring(true);
                ConfigureMonitoring(monitoring);
                return monitoring;
            })
            .AddScoped<PlantUmlGraphGenerator>()
            .AddScoped<IFactory<IClient?, NodeBuildOptions>, ClientFactory>()
            .AddScoped<IFactory<IServer?, NodeBuildOptions>, ServerFactory>()
            .AddScoped<IFactory<INetwork?, NodeBuildOptions>, NetworkFactory>()
            .AddScoped<INetworkBuilder, NetworkBuilder>()
            .AddScoped<IPathFinder, DijkstraPathFinder>()
            .AddScoped<INetworkGraph, NetworkGraph>();
    }

    private static void ConfigureMonitoring(Monitoring monitoring)
    {
        monitoring.Added += (sender, e) =>
            PrintItem(e);

        monitoring.ScopeStarted += (sender, scope) =>
            BeginScope(scope);

        monitoring.ScopeEnded += (sender, scope) =>
            PrintScopeItems(scope);
    }

    private static void BeginScope(MonitoringScope scope)
    {
    }

    private static void PrintScopeItems(MonitoringScope scope)
    {
        if (scope.Parent != null)
        {
            return;
        }

        lock (_lock)
        {
            PrintScope(scope);
        }
    }

    private static void PrintScope(MonitoringScope scope)
    {
        Console.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope.Date:u}>: Begin Scope <{scope.Id}>: \"{scope.ScopeLabel}\"");


        if (scope.Items?.Any() == true)
        {
            foreach (var item in scope.Items)
            {
                if (item.MonitoringItemType == MonitoringItemType.Scope)
                {
                    PrintScope((MonitoringScope)item);
                } else
                {
                    PrintMonitoringItem((MonitoringItem)item);
                }
            }
        }

        Console.WriteLine($"{new string(' ', (scope?.Level ?? 0) * 4)}<{scope.Date:u}>: End Scope <{scope.Id}>: \"{scope.ScopeLabel}\"");
    }

    private static void PrintItem(MonitoringItem item)
    {
        lock (_lock)
        {
            if (item.Scope != null) return;

        Console.WriteLine(
            $"{new string(' ', (item.Scope?.Level + 1 ?? 0) * 4)}<{item.Date:u}>: " +
            $"{{{item.Type}}} {item.SourceType}[\"{item.From}\"] -> {item.DestinationType}[\"{item.To}\"]" +
            $"{(item.PacketSize > 0 ? $", Packet Size = {item.PacketSize}" : "")}" +
            $"{(!string.IsNullOrEmpty(item.Category) ? $", Category = {item.Category}" : "")}.");
        }
    }

    private static void PrintMonitoringItem(MonitoringItem item)
    {
        Console.WriteLine(
            $"{new string(' ', (item.Scope?.Level + 1 ?? 0) * 4)}<{item.Date:u}>: " +
            $"{{{item.Type}}} {item.SourceType}[\"{item.From}\"] -> {item.DestinationType}[\"{item.To}\"]" +
            $"{(item.PacketSize > 0 ? $", Packet Size = {item.PacketSize}" : "")}" +
            $"{(!string.IsNullOrEmpty(item.Category) ? $", Category = {item.Category}" : "")}.");
    }
}