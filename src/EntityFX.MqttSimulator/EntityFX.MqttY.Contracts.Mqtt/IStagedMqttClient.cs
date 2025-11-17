using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Mqtt
{
    public interface IStagedMqttClient
    {
        bool BeginConnect(string server, bool cleanSession = false);

        SessionState CompleteConnect(ResponsePacket<(string Server, bool? CleanSession)> response);

        bool BeginSubscribe(string topicFilter, MqttQos qos);

        void CompleteSubscribe(ResponsePacket<(string TopicFilter, MqttQos Qos)> response);

        bool BeginUnsubscribe(string topicFilter);

        void CompleteUnsubscribe(ResponsePacket<string> response);
    }

}
