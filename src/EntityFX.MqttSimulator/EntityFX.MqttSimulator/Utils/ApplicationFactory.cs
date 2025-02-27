using EntityFX.MqttY.Application.Mqtt;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
namespace EntityFX.MqttY.Utils;

internal class ApplicationFactory : IFactory<IApplication?, object>
{
    private readonly IConfiguration configuration;

    public ApplicationFactory(IConfiguration configuration)
    {
        this.configuration = configuration;
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

        if (options.Protocol == "mqtt")
        {
            var mqttRelayConfSection = configuration.GetSection($"networkGraph:nodes:{options.Name}:configuration");
            var mqttRelayConf = mqttRelayConfSection
                .Get<MqttRelayConfiguration>();

            return new MqttRelay
                (options.Index, options.Name, options.Address ?? options.Name,
                options.Protocol, options.Network, options.NetworkGraph, mqttRelayConf)
            {
                Group = options.Group,
                GroupAmount = options.GroupAmount
            };
        }

        return new Application.Application<object>(options.Index, options.Name, options.Address ?? options.Name,
            options.Protocol, options.Network, options.NetworkGraph, options.Additional)
        {
            Group = options.Group
        };
    }
}
