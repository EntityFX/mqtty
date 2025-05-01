using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Utils;

public interface INodesBuilder
{ 
    Dictionary<string, IFactory<IClient?, NodeBuildOptions<NetworkBuildOption>>> ClientFactory { get; }
    
    Dictionary<string, IFactory<IServer?, NodeBuildOptions<NetworkBuildOption>>> ServerFactory { get; }
    
    IFactory<INetwork?, NodeBuildOptions<NetworkBuildOption>> NetworkFactory { get; }

    Dictionary<string, IFactory<IApplication?, NodeBuildOptions<NetworkBuildOption>>> ApplicationFactory { get; }
    
}
