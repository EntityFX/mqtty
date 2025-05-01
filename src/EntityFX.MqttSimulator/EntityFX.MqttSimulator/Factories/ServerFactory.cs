using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Factories;

public class ServerFactory : IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>
{
    private readonly IServiceProvider _serviceProvider;

    public ServerFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IServer? Configure(NodeBuildOptions<NetworkBuildOption> options, IServer? service)
    {
        service?.Start();
        return service;
    }

    public IServer? Create(NodeBuildOptions<NetworkBuildOption> options)
    {
        if (options.Network == null)
        {
            return null;
        }
        
        return new Server(options.Index, options.Name, options.Address ?? options.Name,
            options.Protocol, options.Specification, options.Network, 
            options.NetworkGraph, options.Additional!.NetworkTypeOption!)
        {
            Group = options.Group
        };
    }
}