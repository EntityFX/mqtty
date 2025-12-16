using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Network;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Factories;

internal class NetworkGraphFactory : IFactory<INetworkSimulator, NetworkGraphFactoryOption>
{
    private readonly IServiceProvider _serviceProvider;

    public NetworkGraphFactory(IServiceProvider serviceProvider)
    {
        this._serviceProvider = serviceProvider;
    }

    public INetworkSimulator Configure(NetworkGraphFactoryOption options, INetworkSimulator service)
    {
        //service.Configure(options.NetworkGraphOption);

        return service;
    }

    public INetworkSimulator Create(NetworkGraphFactoryOption options)
    {
        var nb = _serviceProvider.GetRequiredService<INodesBuilder>();
        var pf = _serviceProvider.GetRequiredService<IPathFinder>();

        var mf = _serviceProvider.GetRequiredService<IFactory<INetworkLogger, NetworkGraphFactoryOption>>();
        var monitoring = mf.Create(options);
        var ng = new NetworkSimulator(pf, monitoring, options.TicksOption, options.EnableCounters)
        {
            OptionsPath = options.OptionsPath
        };

        return ng;
    }
}
