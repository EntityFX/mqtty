
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFX.MqttY.Factories;

public class ApplicationFactory : IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>
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
        application?.Start();
        return application;
    }

    public IApplication? Create(NodeBuildOptions<NetworkBuildOption> options)
    {
        if (options.Network == null)
        {
            return null;
        }

        return new Application.Application<object>(options.Index, options.Name, options.Address ?? options.Name,
            options.Protocol, options.Specification, options.Network, options.NetworkGraph,
            options.Additional!.TicksOptions!, options.Additional)
        {
            Group = options.Group
        };
    }
}
