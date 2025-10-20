using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;

namespace EntityFX.MqttY.Contracts.Utils
{
    public interface IUmlGraphGenerator
    {
        string Generate(INetworkSimulator networkGraph);
        string GenerateSequence(INetworkLogger monitoring, NetworkLoggerScope monitoringScope);
    }
}