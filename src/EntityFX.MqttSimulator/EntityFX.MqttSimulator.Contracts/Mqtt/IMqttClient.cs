using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Contracts.Mqtt
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
