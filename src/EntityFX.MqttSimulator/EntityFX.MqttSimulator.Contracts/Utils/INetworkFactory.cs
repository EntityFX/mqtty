using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Utils;

public interface INetworkBuilder
{
    IFactory<IClient?, NodeBuildOptions<Dictionary<string, string[]>>, Dictionary<string, string[]>> ClientFactory { get; }
    
    IFactory<IServer?, NodeBuildOptions<Dictionary<string, string[]>>, Dictionary<string, string[]>> ServerFactory { get; }
    
    IFactory<INetwork?, NodeBuildOptions<Dictionary<string, string[]>>, Dictionary<string, string[]>> NetworkFactory { get; }

    IFactory<IApplication?, NodeBuildOptions<object>, object> ApplicationFactory { get; }
    
}