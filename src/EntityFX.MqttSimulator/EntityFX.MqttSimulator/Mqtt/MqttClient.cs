using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Mqtt.Internals;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace EntityFX.MqttY.Mqtt
{

    internal class MqttClient : Client, IMqttClient
    {
        private readonly PacketIdProvider packetIdProvider = new();

        private IDictionary<MqttPacketType, Func<string, ushort, IPacket?>> senderRules;

        private ConcurrentDictionary<string, ClientSession> sessionRepository;


        public MqttClient(string name, string address, string protocolType, 
            INetwork network, INetworkGraph networkGraph, string? clientId) 
            : base(name, address, protocolType, network, networkGraph)
        {
            ClientId = clientId ?? Guid.NewGuid().ToString();
            senderRules = DefineSenderRules();
        }

        public string ClientId { get; set; }

        public string Server => serverName;

        public async Task<SessionState> ConnectAsync(string server, MqttQos qos, bool retain = false)
        {
            if (!IsConnected)
            {
                var result = Connect(server);
                if (!result)
                {
                    throw new MqttException($"Unable to server {server}");
                }
            }

            var connect = new ConnectPacket(ClientId, true);

            var response = await SendAsync(connect.PacketToBytes());
            var connAck = response.BytesToPacket<ConnectAckPacket>();

            if (connAck == null)
            {
                throw new MqttException($"No connack");
            }

            if (connAck.Status != MqttConnectionStatus.Accepted)
            {
                throw new MqttConnectException(connAck.Status, connAck.Status.ToString());
            }


            return connAck.SessionPresent ? SessionState.SessionPresent : SessionState.CleanSession; ;
        }

        public Task DisconnectAsync()
        {
            throw new NotImplementedException();
        }

        public Task<bool> PublishAsync(string topic, byte[] payload, MqttQos qos, bool retain = false)
        {
            ushort? packetId = qos == MqttQos.AtMostOnce ? null : (ushort?)packetIdProvider.GetPacketId();
            var publish = new PublishPacket(topic, qos, retain, duplicated: false, packetId: packetId)
            {
                Payload = payload
            };

            return Task.FromResult(true);
        }

        public Task SubscribeAsync(string topicFilter, MqttQos qos)
        {
            throw new NotImplementedException();
        }

        public Task<bool> UnsubscribeAsync(string topicFilter)
        {
            throw new NotImplementedException();
        }

        private Dictionary<MqttPacketType, Func<string, ushort, IPacket?>> DefineSenderRules()
        {
            var senderRules = new Dictionary<MqttPacketType, Func<string, ushort, IPacket?>>
            {
                {
                    MqttPacketType.PublishAck,
                    (clientId, packetId) =>
                    {
                        RemovePendingMessage(clientId, packetId);

                        return default;
                    }
                },
                {
                    MqttPacketType.PublishReceived,
                    (clientId, packetId) =>
                    {
                        RemovePendingMessage(clientId, packetId);

                        return new PublishReleasePacket(packetId);
                    }
                },
                {
                    MqttPacketType.PublishComplete,
                    (clientId, packetId) =>
                    {
                        RemovePendingAcknowledgement(clientId, packetId, MqttPacketType.PublishRelease);

                        return default;
                    }
                }
            };

            return senderRules;
        }

        private void RemovePendingAcknowledgement(string clientId, ushort packetId, MqttPacketType type)
        {
            sessionRepository.TryGetValue(clientId, out ClientSession? session);

            if (session == null)
            {
                throw new MqttException(string.Format($"Client Session {clientId} Not Found", clientId));
            }

            var pendingAcknowledgement = session
                .GetPendingAcknowledgements()
                .FirstOrDefault(u => u.Type == type && u.PacketId == packetId);

            session.RemovePendingAcknowledgement(pendingAcknowledgement);

            sessionRepository.AddOrUpdate(session.Id, session, (key, value) => session);
        }

        private void RemovePendingMessage(string clientId, ushort packetId)
        {
            sessionRepository.TryGetValue(clientId, out ClientSession? session);

            if (session == null)
            {
                throw new MqttException(string.Format($"Client Session {clientId} Not Found", clientId));
            }

            var pendingMessage = session
                .GetPendingMessages()
                .FirstOrDefault(p => p.PacketId.HasValue && p.PacketId.Value == packetId);

            session.RemovePendingMessage(pendingMessage);

            sessionRepository.AddOrUpdate(session.Id, session, (key, value) => session);
        }
    }
}
