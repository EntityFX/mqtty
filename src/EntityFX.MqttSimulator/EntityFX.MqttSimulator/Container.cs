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
                var monitoring = new Monitoring();
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
            PrintMonitoringItem(e);

        monitoring.ScopeStarted += (sender, scope) =>
            BeginScope(scope);

        monitoring.ScopeEnded += (sender, scope) =>
            PrintEndScope(scope);
    }

    private static void BeginScope(MonitoringScope scope)
    {
    }

    private static void PrintEndScope(MonitoringScope scope)
    {
        PrintScopeItems(scope);
    }

    private static void PrintScopeItems(MonitoringScope scope)
    {
        lock (_lock)
        {
            Console.WriteLine($"<{scope.Date:u}>: Begin scope <{scope.Id}>: \"{scope.Name}\"");

            if (scope.Items?.Any() == true)
            {
                foreach (var item in scope.Items)
                {
                    Console.WriteLine(
                        $"{new string(' ', (item.Scope?.Level + 1 ?? 0) * 4)}<{item.Date:u}>: " +
                        $"{{{item.Type}}} {item.SourceType}[\"{item.From}\"] -> {item.DestinationType}[\"{item.To}\"]" +
                        $"{(item.PacketSize > 0 ? $", Packet Size = {item.PacketSize}" : "")}" +
                        $"{(!string.IsNullOrEmpty(item.Category) ? $", Category = {item.Category}" : "")}.");
                }
            }

            Console.WriteLine($"<{scope.Date:u}>: End scope <{scope.Id}>: \"{scope.Name}\"");
        }
    }

    private static void PrintMonitoringItem(MonitoringItem item)
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
}