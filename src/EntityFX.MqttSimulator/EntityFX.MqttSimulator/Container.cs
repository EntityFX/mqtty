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
            .AddScoped<IMonitoring>((sp) => ConfigureMonitoring(sp))
            .AddScoped<PlantUmlGraphGenerator>()
            .AddScoped<IFactory<IClient?, NodeBuildOptions<Dictionary<string, string[]>>, Dictionary<string, string[]>>, ClientFactory>()
            .AddScoped<IFactory<IServer?, NodeBuildOptions<Dictionary<string, string[]>>, Dictionary<string, string[]>>, ServerFactory>()
            .AddScoped<IFactory<INetwork, NodeBuildOptions<Dictionary<string, string[]>>, Dictionary<string, string[]>>, NetworkFactory>()
            .AddScoped<INetworkBuilder, NetworkBuilder>()
            .AddScoped<IPathFinder, DijkstraPathFinder>()
            .AddScoped<INetworkGraph, NetworkGraph>();
    }

    private static IMonitoring ConfigureMonitoring(IServiceProvider sp)
    {
        var monitoring = new Monitoring(true);
        var consoleProvider = new ConsoleMonitoringProvider(monitoring);
        consoleProvider.Start();

        return monitoring;
    }
}