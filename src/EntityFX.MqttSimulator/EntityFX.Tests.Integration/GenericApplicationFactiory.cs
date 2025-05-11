using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.Tests.Integration
{
    public class GenericApplicationFactiory : IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>
    {
        public IApplication? Configure(NodeBuildOptions<NetworkBuildOption> options, IApplication? service)
        {
            return service;
        }

        public IApplication? Create(NodeBuildOptions<NetworkBuildOption> options)
        {
            return new Application<NetworkBuildOption>(options.Index, options.Name, options.Address ?? options.Name,
                options.Protocol, options.Specification, options.Network!, options.NetworkGraph, 
                options.Additional!.TicksOptions!, options.Additional)
            {
                Group = options.Group
            };
        }
    }
}