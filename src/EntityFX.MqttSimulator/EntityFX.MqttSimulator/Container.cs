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
using EntityFX.MqttY.Network;
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
            .AddScoped<IFactory<INetworkLogger, NetworkGraphFactoryOption>, MonitoringFactory>()
            .AddScoped<IFactory<INetworkGraph, NetworkGraphFactoryOption>, NetworkGraphFactory>()
            .AddScoped<IFactory<IClient?, NodeBuildOptions<Dictionary<string, string[]>>>, ClientFactory>()
            .AddScoped<IFactory<IServer?, NodeBuildOptions<Dictionary<string, string[]>>>, ServerFactory>()
            .AddScoped<IFactory<INetwork, NodeBuildOptions<(TicksOptions TicksOptions, Dictionary<string, string[]> Additional)>>, NetworkFactory>()
            .AddScoped<IFactory<IApplication?, NodeBuildOptions<object>>, ApplicationFactory>()
            .AddScoped<IFactory<IScenario?, (string Scenario, IDictionary<string, ScenarioOption> Options)>, ScenarioFactory>()
            .AddScoped<INetworkBuilder, NetworkBuilder>()
            .AddScoped<IPathFinder, DijkstraPathFinder>()
            .AddScoped<IMqttPacketManager, MqttNativePacketManager>()
            //.AddScoped<IMqttPacketManager, MqttJsonPacketManager>()
            .AddScoped<IMqttTopicEvaluator, MqttTopicEvaluator>((serviceProvider) => new MqttTopicEvaluator(true));
    }
}