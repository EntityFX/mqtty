using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Plugin.Mqtt.Contracts
{
    public interface IMqttBroker : INode
    {
        void Start();

        void Stop();
    }

}
