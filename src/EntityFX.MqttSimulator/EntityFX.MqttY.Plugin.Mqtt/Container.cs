using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Plugin.Mqtt;

public static class Container
{
    
    public static IServiceCollection ConfigureMqttServices(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddSingleton<IBrokerBenchmarkRepository, BrokerBenchmarkRepository>()
            .AddScoped<IMqttPacketManager, MqttNativePacketManager>()
            //.AddScoped<IMqttPacketManager, MqttJsonPacketManager>()
            .AddScoped<IMqttTopicEvaluator, MqttTopicEvaluator>((serviceProvider) => new MqttTopicEvaluator(true));
    }
}
