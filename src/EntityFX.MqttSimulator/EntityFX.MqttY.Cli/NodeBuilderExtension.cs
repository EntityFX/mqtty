using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Factories;
using EntityFX.MqttY.Plugin.Mqtt.Factories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class NodeBuilderExtension
{
    
    public static IServiceCollection ConfigureNodesBuilder(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddScoped<INodesBuilder>(sp => CreateNodesBuilder(sp));

        return serviceCollection;
    }

    private static INodesBuilder CreateNodesBuilder(IServiceProvider sp)
    {
        return new NodesBuilder(
            new Dictionary<string, IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>>()
                { ["net"] = new ClientFactory(), ["mqtt"] = new MqttClientFactory(), },
            new Dictionary<string, IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>>()
                { ["net"] = new ServerFactory(sp), ["mqtt"] = new MqttServerFactory(sp), },
            new Dictionary<string, IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>>()
            {
                ["net"] = new ApplicationFactory(sp.GetRequiredService<IConfiguration>(), sp),
                ["mqtt"] = new MqttApplicationFactory(sp.GetRequiredService<IConfiguration>(), sp),
            }, sp.GetRequiredService<IFactory<INetwork, NodeBuildOptions<NetworkBuildOption>>>()!);
    }
}