using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Contracts
{
    public interface IMqttClient : IClient
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
