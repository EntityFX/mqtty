using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Mqtt
{
    public interface IMqttBroker : INode
    {
        void Start();

        void Stop();
    }

}
