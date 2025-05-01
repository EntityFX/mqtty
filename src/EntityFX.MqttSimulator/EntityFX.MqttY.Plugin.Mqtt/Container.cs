using EntityFX.MqttY.Plugin.Mqtt.Contracts;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Formatters;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using EntityFX.MqttY.Plugin.Mqtt.Internals.Formatters;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Plugin.Mqtt;

public static class Container
{
    
    public static IServiceCollection ConfigureMqttServices(this IServiceCollection serviceCollection)
    {
        return serviceCollection
            .AddScoped<IMqttPacketManager, MqttNativePacketManager>()
            //.AddScoped<IMqttPacketManager, MqttJsonPacketManager>()
            .AddScoped<IMqttTopicEvaluator, MqttTopicEvaluator>((serviceProvider) => new MqttTopicEvaluator(true));
    }
}