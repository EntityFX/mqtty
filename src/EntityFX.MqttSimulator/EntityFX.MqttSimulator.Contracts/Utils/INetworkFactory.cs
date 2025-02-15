using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Utils;

public interface INetworkBuilder
{
    IFactory<IClient?, NodeBuildOptions> ClientFactory { get; }
    
    IFactory<IServer?, NodeBuildOptions> ServerFactory { get; }
    
    IFactory<INetwork?, NodeBuildOptions> NetworkFactory { get; }
    
}