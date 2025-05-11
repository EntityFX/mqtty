using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Factories;

public class ClientFactory : IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>
{
    public IClient? Configure(NodeBuildOptions<NetworkBuildOption> options, IClient? service)
    {
        if (string.IsNullOrEmpty(options.ConnectsTo))
        {
            return service;
        }

        service?.Connect(options.ConnectsTo);


        return service;
    }

    public IClient? Create(NodeBuildOptions<NetworkBuildOption> options)
    {
        if (options.Network == null)
        {
            return null;
        }
        
        return new Client(options.Index, options.Name, options.Address ?? options.Name, options.Protocol,
            options.Specification,
            options.Network, options.NetworkGraph, options.Additional!.NetworkTypeOption!, 
            options.Additional!.TicksOptions!)
        {
            Group = options.Group
        };
    }
}