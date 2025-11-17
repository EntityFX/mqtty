using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Mqtt
{
    public interface IStagedMqttClient
    {
        bool BeginConnect(string server, bool cleanSession = false);

        SessionState CompleteConnect(NetworkPacket? response, string server, bool cleanSession = false);

        bool BeginSubscribe(string topicFilter, MqttQos qos);

        void CompleteSubscribe(NetworkPacket? response, string topicFilter, MqttQos qos);

        bool BeginUnsubscribe(string topicFilter);

        void CompleteUnsubscribe(NetworkPacket? response, string topicFilter);
    }
}
