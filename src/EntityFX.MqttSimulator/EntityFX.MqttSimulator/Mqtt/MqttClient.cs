using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Mqtt.Internals;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace EntityFX.MqttY.Mqtt
{

    internal class MqttClient : Client, IMqttClient
    {
        private readonly PacketIdProvider packetIdProvider = new();

        private readonly ConcurrentDictionary<string, ClientSession> sessionRepository;

        private IDictionary<MqttPacketType, Func<string, ushort, IPacket?>> senderRules;


        public MqttClient(string name, string address, string protocolType, 
            INetwork network, INetworkGraph networkGraph, string? clientId) 
            : base(name, address, protocolType, network, networkGraph)
        {
            ClientId = clientId ?? name;
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

            var response = await SendAsync(connect.PacketToBytes(), "Connect");
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

        public async Task<bool> PublishAsync(string topic, byte[] payload, MqttQos qos, bool retain = false)
        {
            ushort? packetId = qos == MqttQos.AtMostOnce ? null : (ushort?)packetIdProvider.GetPacketId();
            var publish = new PublishPacket(topic, qos, retain, duplicated: false, packetId: packetId)
            {
                Payload = payload
            };

            if (!IsConnected)
            {
                SaveMessage(publish, ClientId, PendingMessageStatus.PendingToSend);
                return true;
            }

            var response = await SendAsync(publish.PacketToBytes(), "Publish");

            if (qos == MqttQos.AtLeastOnce)
            {
                var publishAck = response.BytesToPacket<PublishAckPacket>();
            }
            else if (qos == MqttQos.ExactlyOnce)
            {
                var publishReceived = response.BytesToPacket<PublishReceivedPacket>();

                var publishRelease = new PublishReleasePacket(packetId ?? 0);

                var completeResponse = await SendAsync(publishRelease.PacketToBytes(), "PublishRelease");

                var publishComplete = completeResponse.BytesToPacket<PublishCompletePacket>();
            }

            return true;
        }

        public async Task SubscribeAsync(string topicFilter, MqttQos qos)
        {
            var packetId = packetIdProvider.GetPacketId();
            var subscribe = new SubscribePacket(packetId, new[] { new Subscription(topicFilter, qos) });

            var subscribeTimeout = TimeSpan.FromSeconds(60);

            var response = await SendAsync(subscribe.PacketToBytes(), "Subscribe");
            var subscribeAck = response.BytesToPacket<SubscribeAckPacket>();

            if (subscribeAck == null)
            {
                throw new MqttClientException($"Subscription Disconnected: {Id}, {topicFilter}");
            }

            if (subscribeAck.ReturnCodes.FirstOrDefault() == SubscribeReturnCode.Failure)
            {
                throw new MqttClientException($"Subscription Rejected: {Id}, {topicFilter}");
            }
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

        private void SaveMessage(PublishPacket message, string clientId, PendingMessageStatus status)
        {
            if (message.QualityOfService == MqttQos.AtMostOnce)
            {
                return;
            }

            
            sessionRepository.TryGetValue(clientId, out ClientSession? session);

            if (session == null)
            {
                throw new MqttException(string.Format($"Client Session {clientId} Not Found", clientId));
            }

            var savedMessage = new PendingMessage
            {
                Status = status,
                QualityOfService = message.QualityOfService,
                Duplicated = message.Duplicated,
                Retain = message.Retain,
                Topic = message.Topic,
                PacketId = message.PacketId,
                Payload = message.Payload
            };

            session.AddPendingMessage(savedMessage);

            sessionRepository.AddOrUpdate(session.Id, session, (key, value) => session);
        }
    }
}
