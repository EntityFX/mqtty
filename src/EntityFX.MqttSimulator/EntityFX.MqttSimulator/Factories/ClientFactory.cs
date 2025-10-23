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
        //options.NetworkGraph
        //options.Network, 
        var client = new Client(options.Index, options.Name, options.Address ?? options.Name, options.Protocol,
            options.Specification,
            options.Additional!.TicksOptions!)
        {
            Group = options.Group
        };

        options.Network.AddClient(client);
        options.NetworkGraph.AddClient(client);

        return client;
    }
}