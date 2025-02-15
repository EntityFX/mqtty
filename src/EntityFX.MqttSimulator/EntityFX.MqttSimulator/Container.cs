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
                monitoring.Added += (sender, e) =>
                    Console.WriteLine($"<{e.Date:u}>, {{{e.Type}}} {e.SourceType}[\"{e.From}\"] -> {e.DestinationType}[\"{e.To}\"]" +
                                      $"{(e.PacketSize > 0 ? $", Packet Size = {e.PacketSize}" : "")}" +
                                      $"{(!string.IsNullOrEmpty(e.Category) ? $", Category = {e.Category}" : "")}.");
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
}