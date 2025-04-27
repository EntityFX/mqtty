using EntityFX.MqttY.Application.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;

namespace EntityFX.MqttY.Factories;

internal class ApplicationFactory : IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public ApplicationFactory(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    public IApplication? Configure(NodeBuildOptions<NetworkBuildOption> options, IApplication? application)
    {

        application?.StartAsync().Wait();
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

        if (options is { Protocol: "mqtt", Specification: "mqtt-relay" })
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

        if (options is { Protocol: "mqtt", Specification: "mqtt-receiver" })
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

        return new Application.Application<object>(options.Index, options.Name, options.Address ?? options.Name,
            options.Protocol, options.Specification, options.Network, options.NetworkGraph, options.Additional)
        {
            Group = options.Group
        };
    }
}
