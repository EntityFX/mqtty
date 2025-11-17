using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Mqtt
{
    public interface IMqttClient : IClient, IStagedMqttClient
    {
        string Server { get; }

        string ClientId { get; }

        event EventHandler<MqttMessage>? MessageReceived;

        SessionState Connect(string server, bool cleanSession = false);

        bool Publish(string topic, byte[] payload, MqttQos qos, bool retain = false);

        void Subscribe(string topicFilter, MqttQos qos);

        bool Unsubscribe(string topicFilter);
    }
}
