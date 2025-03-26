using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Contracts.Utils;

public interface INetworkBuilder
{
    IFactory<IClient?, NodeBuildOptions<Dictionary<string, string[]>>> ClientFactory { get; }
    
    IFactory<IServer?, NodeBuildOptions<Dictionary<string, string[]>>> ServerFactory { get; }
    
    IFactory<INetwork?, NodeBuildOptions<(TicksOptions TicksOptions, Dictionary<string, string[]> Additional)>> NetworkFactory { get; }

    IFactory<IApplication?, NodeBuildOptions<object>> ApplicationFactory { get; }
    
}