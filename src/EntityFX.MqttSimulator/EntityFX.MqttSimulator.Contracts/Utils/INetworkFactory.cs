using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Utils;

public interface INetworkBuilder
{
    IFactory<IClient?, Dictionary<string, string[]>> ClientFactory { get; }
    
    IFactory<IServer?, Dictionary<string, string[]>> ServerFactory { get; }
    
    IFactory<INetwork?, Dictionary<string, string[]>> NetworkFactory { get; }

    IFactory<IApplication?, object> ApplicationFactory { get; }
    
}