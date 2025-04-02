using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Network;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Factories;

internal class NetworkGraphFactory : IFactory<INetworkGraph, NetworkGraphFactoryOption>
{
    private readonly IServiceProvider serviceProvider;

    public NetworkGraphFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public INetworkGraph Configure(NetworkGraphFactoryOption options, INetworkGraph service)
    {
        service.Configure(options.NetworkGraphOption);

        return service;
    }

    public INetworkGraph Create(NetworkGraphFactoryOption options)
    {
        var nb = serviceProvider.GetRequiredService<INetworkBuilder>();
        var pf = serviceProvider.GetRequiredService<IPathFinder>();

        var mf = serviceProvider.GetRequiredService<IFactory<IMonitoring, MonitoringOption>>();
        var monitoring = mf.Create(options.MonitoringOption);
        var ng = new NetworkGraph(serviceProvider, nb, pf, monitoring)
        {
            OptionsPath = options.OptionsPath
        };

        return ng;
    }
}
