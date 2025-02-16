using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY;

public static class Container
{
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
            Console.WriteLine(
                $"{new string (' ', e.Scope?.Level ?? 0)}<{e.Date:u}>: " +
                $"{{{e.Type}}} {e.SourceType}[\"{e.From}\"] -> {e.DestinationType}[\"{e.To}\"]" +
                $"{(e.PacketSize > 0 ? $", Packet Size = {e.PacketSize}" : "")}" +
                $"{(!string.IsNullOrEmpty(e.Category) ? $", Category = {e.Category}" : "")}.");

        monitoring.ScopeStarted += (sender, scope) =>
            Console.WriteLine($"<{scope.Date:u}>: Begin scope <{scope.Id}>: \"{scope.Name}\"");
        
        monitoring.ScopeEnded += (sender, scope) =>
            Console.WriteLine($"<{scope.Date:u}>: End scope <{scope.Id}>: \"{scope.Name}\"");
    }
}