using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Utils;

public interface INetworkGraphFormatter
{
    string SerializeNetworkGraph(INetworkSimulator networkGraph);
}