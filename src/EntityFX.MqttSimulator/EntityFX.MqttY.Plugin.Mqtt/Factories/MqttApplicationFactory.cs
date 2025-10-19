using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Plugin.Mqtt.Application.Mqtt;
using EntityFX.MqttY.Plugin.Mqtt.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Plugin.Mqtt.Factories;

public class MqttApplicationFactory : IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public MqttApplicationFactory(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    public IApplication? Configure(NodeBuildOptions<NetworkBuildOption> options, IApplication? application)
    {

        application?.Start();
        return application;
    }

    public IApplication? Create(NodeBuildOptions<NetworkBuildOption> options)
    {
        if (options.Network == null)
        {
            return null;
        }

        var applicationConfigurationPath = $"{(string.IsNullOrEmpty(options.OptionsPath) ? "" : $"{options.OptionsPath}:")}" +
                                           $"graph:nodes:{options.Name}:configuration";
        var configurationSection = _configuration.GetSection(applicationConfigurationPath);

        switch (options)
        {
            case {  Specification: "mqtt-relay" }:
            {
                var mqttRelayConf = configurationSection
                    .Get<MqttRelayConfiguration>();

                var mqttTopicEvaluator = _serviceProvider.GetRequiredService<IMqttTopicEvaluator>();
                var networkSimulatorBuilder = _serviceProvider.GetRequiredService<INetworkSimulatorBuilder>();

                return new MqttRelay
                (options.Index, options.Name, options.Address ?? options.Name,
                    options.Protocol, options.Specification, options.Network, networkSimulatorBuilder, mqttTopicEvaluator,
                    options.Additional!.TicksOptions!,
                    mqttRelayConf)
                {
                    Group = options.Group,
                    GroupAmount = options.GroupAmount
                };
            }
            case {  Specification: "mqtt-receiver" }:
            {
                var mqttReceiverConf = configurationSection
                    .Get<MqttReceiverConfiguration>();

                var networkSimulatorBuilder = _serviceProvider.GetRequiredService<INetworkSimulatorBuilder>();

                return new MqttReceiver
                (networkSimulatorBuilder, options.Index, options.Name, options.Address ?? options.Name,
                    options.Protocol, options.Specification, options.Network, options.NetworkGraph,
                    options.Additional!.TicksOptions!, options.Additional.NetworkTypeOption!, mqttReceiverConf)
                {
                    Group = options.Group,
                    GroupAmount = options.GroupAmount
                };
            }
            default: return null;
        }
    }
}
