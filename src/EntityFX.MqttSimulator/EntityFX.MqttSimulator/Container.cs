using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY;

public static class Container
{
 

    public static IServiceCollection ConfigureServices(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddScoped<PlantUmlGraphGenerator>()
            .AddScoped<IFactory<INetworkLogger, NetworkGraphFactoryOption>, NetworkLoggerFactory>()
            .AddScoped<IFactory<INetworkSimulator, NetworkGraphFactoryOption>, NetworkGraphFactory>()
            .AddScoped<IFactory<INetwork, NodeBuildOptions<NetworkBuildOption>>, NetworkFactory>()
            .AddScoped<INetworkSimulatorBuilder, NetworkSimulatorBuilder>()
            .AddScoped<IUmlGraphGenerator, PlantUmlGraphGenerator>()
            .AddScoped<IPathFinder, DijkstraPathFinder>();
    }
}