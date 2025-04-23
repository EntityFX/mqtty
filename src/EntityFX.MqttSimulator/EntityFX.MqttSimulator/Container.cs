using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Mqtt.Internals;
using EntityFX.MqttY.Mqtt.Internals.Formatters;
using EntityFX.MqttY.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

namespace EntityFX.MqttY;

public static class Container
{
 

    public static IServiceCollection ConfigureServices(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddScoped<PlantUmlGraphGenerator>()
            .AddScoped<IFactory<INetworkLogger, NetworkGraphFactoryOption>, NetworkLoggerFactory>()
            .AddScoped<IFactory<INetworkSimulator, NetworkGraphFactoryOption>, NetworkGraphFactory>()
            .AddScoped<IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>, ClientFactory>()
            .AddScoped<IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>, ServerFactory>()
            .AddScoped<IFactory<INetwork, NodeBuildOptions<NetworkBuildOption>>, NetworkFactory>()
            .AddScoped<IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>, ApplicationFactory>()
            .AddScoped<IFactory<IScenario?, (string Scenario, IDictionary<string, ScenarioOption> Options)>, ScenarioFactory>()
            .AddScoped<INetworkBuilder, NetworkBuilder>()
            .AddScoped<INetworkSimulatorBuilder, NetworkSimulatorBuilder>()
            .AddScoped<IPathFinder, DijkstraPathFinder>()
            .AddScoped<IMqttPacketManager, MqttNativePacketManager>()
            //.AddScoped<IMqttPacketManager, MqttJsonPacketManager>()
            .AddScoped<IMqttTopicEvaluator, MqttTopicEvaluator>((serviceProvider) => new MqttTopicEvaluator(true));
    }
}