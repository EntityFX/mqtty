using System.Text;
using System.Text.Json;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Plugin.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Engines.Execution;

/// <summary>
/// Строит ServiceProvider для исполнения симуляции. IConfiguration формируется из
/// NetworkGraphOption так, чтобы секция graph:nodes:{name}:configuration была доступна
/// фабрикам приложений (MqttApplicationFactory читает конфигурацию relay-узлов оттуда).
/// </summary>
public static class EngineServiceProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static ServiceProvider Create(NetworkGraphOption? graph = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(_ => BuildConfiguration(graph));
        services.ConfigureServices();
        services.ConfigureMqttServices();
        services.AddScoped<INodesBuilder>(sp => CreateNodesBuilder(sp));
        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfiguration(NetworkGraphOption? graph)
    {
        if (graph == null)
        {
            return new ConfigurationBuilder().Build();
        }

        var graphJson = JsonSerializer.Serialize(graph, JsonOptions);
        var wrapped = $"{{\"graph\":{graphJson}}}";
        return new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(wrapped)))
            .Build();
    }

    private static INodesBuilder CreateNodesBuilder(IServiceProvider sp)
    {
        var networkFactory = sp.GetRequiredService<IFactory<INetwork, NodeBuildOptions<NetworkBuildOption>>>();
        return new NodesBuilder(
            new Dictionary<string, IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>>
            {
                ["net"] = new ClientFactory(),
                ["mqtt"] = new MqttClientFactory(),
            },
            new Dictionary<string, IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>>
            {
                ["net"] = new ServerFactory(sp),
                ["mqtt"] = new MqttServerFactory(sp),
            },
            new Dictionary<string, IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>>
            {
                ["net"] = new ApplicationFactory(sp.GetRequiredService<IConfiguration>(), sp),
                ["mqtt"] = new MqttApplicationFactory(sp.GetRequiredService<IConfiguration>(), sp),
            },
            (IFactory<INetwork?, NodeBuildOptions<NetworkBuildOption>>)networkFactory);
    }
}
