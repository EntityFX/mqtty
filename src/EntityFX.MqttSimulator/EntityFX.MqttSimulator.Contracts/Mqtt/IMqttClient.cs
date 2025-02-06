using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Contracts.Mqtt
{


    public interface IMqttClient
    {
        string ClientId { get; }

        Task<MqttSession> ConnectAsync(MqttQos qos, bool retain = false);

        Task DisconnectAsync();

        Task<bool> PublishAsync(string topic, IEnumerable<byte> payload);

        Task SubscribeAsync(string topicFilter, MqttQos qos);

        Task<bool> UnsubscribeAsync(string topicFilter);
    }
}
