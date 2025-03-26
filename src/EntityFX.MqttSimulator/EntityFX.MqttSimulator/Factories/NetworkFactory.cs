using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

internal class NetworkFactory : IFactory<INetwork, NodeBuildOptions<
    (TicksOptions TicksOptions, Dictionary<string, string[]> Additional)>>
{
    public INetwork Configure(
        NodeBuildOptions<(TicksOptions TicksOptions, Dictionary<string, string[]> Additional)> options,
        INetwork service)
    {
        return service;
    }

    public INetwork Create(
        NodeBuildOptions<(TicksOptions TicksOptions, Dictionary<string, string[]> Additional)> options)
    {
        return new Network.Network(options.Index, 
            options.Name, options.Address ?? options.Name, options.NetworkGraph, 
            options.Additional.TicksOptions);
    }
}
