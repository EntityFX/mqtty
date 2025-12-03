using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Mqtt
{
    public interface IStagedMqttClient
    {
        bool BeginConnect(string server, bool cleanSession = false);

        SessionState CompleteConnect(INetworkPacket? response, string server, bool cleanSession = false);

        bool BeginSubscribe(string topicFilter, MqttQos qos);

        void CompleteSubscribe(INetworkPacket? response, string topicFilter, MqttQos qos);

        bool BeginUnsubscribe(string topicFilter);

        void CompleteUnsubscribe(INetworkPacket? response, string topicFilter);
    }
}
