using EntityFX.MqttY.Application.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
namespace EntityFX.MqttY.Utils;

internal class ApplicationFactory : IFactory<IApplication?, object>
{
    private readonly IConfiguration configuration;
    private readonly IServiceProvider serviceProvider;

    public ApplicationFactory(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        this.configuration = configuration;
        this.serviceProvider = serviceProvider;
    }

    public IApplication? Configure(NodeBuildOptions<object> options, IApplication? application)
    {

        application?.StartAsync().Wait();
        return application;
    }

    public IApplication? Create(NodeBuildOptions<object> options)
    {
        if (options.Network == null)
        {
            return null;
        }

        if (options.Protocol == "mqtt" && options.Specification == "mqtt-relay")
        {
            var mqttRelayConfSection = configuration.GetSection($"networkGraph:nodes:{options.Name}:configuration");
            var mqttRelayConf = mqttRelayConfSection
                .Get<MqttRelayConfiguration>();

            var mqttTopicEvaluator = serviceProvider.GetRequiredService<IMqttTopicEvaluator>();

            return new MqttRelay
                (options.Index, options.Name, options.Address ?? options.Name,
                options.Protocol, options.Specification, options.Network, options.NetworkGraph, mqttTopicEvaluator, mqttRelayConf)
            {
                Group = options.Group,
                GroupAmount = options.GroupAmount
            };
        }

        if (options.Protocol == "mqtt" && options.Specification == "mqtt-receiver")
        {
            var mqttReceiverConfSection = configuration.GetSection($"networkGraph:nodes:{options.Name}:configuration");
            var mqttReceiverConf = mqttReceiverConfSection
                .Get<MqttReceiverConfiguration>();

            return new MqttReceiver
                (options.Index, options.Name, options.Address ?? options.Name,
                options.Protocol, options.Specification, options.Network, options.NetworkGraph, mqttReceiverConf)
            {
                Group = options.Group,
                GroupAmount = options.GroupAmount
            };
        }

        return new Application.Application<object>(options.Index, options.Name, options.Address ?? options.Name,
            options.Protocol, options.Specification, options.Network, options.NetworkGraph, options.Additional)
        {
            Group = options.Group
        };
    }
}
