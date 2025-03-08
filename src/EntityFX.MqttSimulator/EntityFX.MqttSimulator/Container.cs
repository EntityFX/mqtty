using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Scenarios;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Mqtt.Internals;
using EntityFX.MqttY.Network;
using EntityFX.MqttY.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY;

public static class Container
{
 

    public static IServiceCollection ConfigureServices(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddScoped<IMonitoring>((sp) => ConfigureMonitoring(sp))
            .AddScoped<PlantUmlGraphGenerator>()
            .AddScoped<IFactory<IClient?, NodeBuildOptions<Dictionary<string, string[]>>>, ClientFactory>()
            .AddScoped<IFactory<IServer?, NodeBuildOptions<Dictionary<string, string[]>>>, ServerFactory>()
            .AddScoped<IFactory<INetwork, NodeBuildOptions<Dictionary<string, string[]>>>, NetworkFactory>()
            .AddScoped<IFactory<IApplication?, NodeBuildOptions<object>>, ApplicationFactory>()
            .AddScoped<IFactory<IScenario?, (string Scenario, IDictionary<string, ScenarioOption> Options)>, ScenarioFactory>()
            .AddScoped<INetworkBuilder, NetworkBuilder>()
            .AddScoped<IPathFinder, DijkstraPathFinder>()
            .AddScoped<INetworkGraph, NetworkGraph>()
            .AddScoped<IMqttTopicEvaluator, MqttTopicEvaluator>((serviceProvider) => new MqttTopicEvaluator(true));
    }

    private static IMonitoring ConfigureMonitoring(IServiceProvider sp)
    {
        var monitoring = new Monitoring(true);
        var consoleProvider = new ConsoleMonitoringProvider(monitoring);
        consoleProvider.Start();

        return monitoring;
    }
}