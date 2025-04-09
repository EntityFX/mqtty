using EntityFX.MqttY.Application;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.Tests.Integration
{
    public class GenericApplicationFactiory : IFactory<IApplication?, NodeBuildOptions<object>>
    {
        public IApplication? Configure(NodeBuildOptions<object> options, IApplication? service)
        {
            return service;
        }

        public IApplication? Create(NodeBuildOptions<object> options)
        {
            return new Application<object>(options.Index, options.Name, options.Address ?? options.Name,
                options.Protocol, options.Specification, options.Network!, options.NetworkGraph, options.Additional)
            {
                Group = options.Group
            };
        }
    }
}