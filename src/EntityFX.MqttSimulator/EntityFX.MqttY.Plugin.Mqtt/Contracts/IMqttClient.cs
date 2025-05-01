using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Plugin.Mqtt.Contracts.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Contracts
{
    public interface IMqttClient : IClient
    {
        string Server { get; }

        string ClientId { get; }

        event EventHandler<MqttMessage>? MessageReceived;

        Task<SessionState> ConnectAsync(string server, bool cleanSession = false);

        Task DisconnectAsync();

        Task<bool> PublishAsync(string topic, byte[] payload, MqttQos qos, bool retain = false);

        Task SubscribeAsync(string topicFilter, MqttQos qos);

        Task<bool> UnsubscribeAsync(string topicFilter);
    }
}
