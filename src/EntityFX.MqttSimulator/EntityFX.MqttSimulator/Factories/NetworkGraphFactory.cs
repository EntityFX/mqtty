using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;
using EntityFX.MqttY.Network;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Factories;

internal class NetworkGraphFactory : IFactory<INetworkSimulator, NetworkGraphFactoryOption>
{
    private readonly IServiceProvider serviceProvider;

    public NetworkGraphFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    public INetworkSimulator Configure(NetworkGraphFactoryOption options, INetworkSimulator service)
    {
        //service.Configure(options.NetworkGraphOption);

        return service;
    }

    public INetworkSimulator Create(NetworkGraphFactoryOption options)
    {
        var nb = serviceProvider.GetRequiredService<INetworkBuilder>();
        var pf = serviceProvider.GetRequiredService<IPathFinder>();

        var mf = serviceProvider.GetRequiredService<IFactory<INetworkLogger, NetworkGraphFactoryOption>>();
        var monitoring = mf.Create(options);
        var ng = new NetworkGraph(serviceProvider, nb, pf, monitoring, options.TicksOption)
        {
            OptionsPath = options.OptionsPath
        };

        return ng;
    }
}
