using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using System.Text;
using System.Text.Json;

namespace EntityFX.MqttY.Mqtt
{
    internal class MqttClient : Client, IMqttClient
    {
        public MqttClient(string name, string address, string protocolType, 
            INetwork network, INetworkGraph networkGraph, string? clientId) 
            : base(name, address, protocolType, network, networkGraph)
        {
            ClientId = clientId ?? Guid.NewGuid().ToString();
        }

        public string ClientId { get; set; }

        public string Server => serverName;

        public async Task<MqttSession?> ConnectAsync(string server, MqttQos qos, bool retain = false)
        {
            if (!IsConnected)
            {
                var result = Connect(server);
                if (!result) return null;
            }

            var connect = new ConnectPacket(ClientId, true);
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(connect));

            await SendAsync(bytes);

            return new MqttSession();
        }

        public Task DisconnectAsync()
        {
            throw new NotImplementedException();
        }

        public Task<bool> PublishAsync(string topic, IEnumerable<byte> payload)
        {
            throw new NotImplementedException();
        }

        public Task SubscribeAsync(string topicFilter, MqttQos qos)
        {
            throw new NotImplementedException();
        }

        public Task<bool> UnsubscribeAsync(string topicFilter)
        {
            throw new NotImplementedException();
        }
    }
}
