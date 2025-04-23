using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Utils;

public interface INetworkBuilder
{ 
    IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>> ClientFactory { get; }
    
    IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>> ServerFactory { get; }
    
    IFactory<INetwork?, NodeBuildOptions<NetworkBuildOption>> NetworkFactory { get; }

    IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>> ApplicationFactory { get; }
    
}
