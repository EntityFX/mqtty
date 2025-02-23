using EntityFX.MqttY.Contracts.Monitoring;
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
using static System.Formats.Asn1.AsnWriter;

namespace EntityFX.MqttY.Mqtt
{

    internal class MqttClient : Client, IMqttClient
    {
        private readonly PacketIdProvider _packetIdProvider = new();

        private readonly IRepository<ClientSession> _sessionRepository
            = new InMemoryRepository<ClientSession>();

        private IDictionary<MqttPacketType, Func<string, ushort, IPacket?>> _senderRules;

        public event EventHandler<MqttMessage> MessageReceived;

        public MqttClient(int index, string name, string address, string protocolType,
            INetwork network, INetworkGraph networkGraph, string? clientId)
            : base(index, name, address, protocolType, network, networkGraph)
        {
            ClientId = clientId ?? name;
        }

        public string ClientId { get; set; }

        public string Server => serverName;

        public async Task<SessionState> ConnectAsync(string server, bool cleanSession = false)
        {
            var connect = new ConnectPacket(ClientId, true);
            var payload = GetPacket(server, NodeType.Server, connect.PacketToBytes(), "MQTT Connect");

            if (IsConnected)
            {
                return !cleanSession ? SessionState.SessionPresent : SessionState.CleanSession;
            }

            var scope = NetworkGraph.Monitoring.WithBeginScope(ref payload!, $"MQTT Client {ClientId} connects to broker {server}");
            NetworkGraph.Monitoring.Push(payload, MonitoringType.Connect, $"MQTT Client {ClientId} connects to broker {server}");
            var response = await ConnectImplementationAsync(server, payload);
            NetworkGraph.Monitoring.WithEndScope(ref response!);

            if (response == null)
            {
                throw new MqttException($"Unable connect to broker {server}");
            }


            OpenClientSession(cleanSession);


            if (response == null)
            {
                throw new MqttException($"No connack");
            }

            var connAck = response.Payload.BytesToPacket<ConnectAckPacket>();

            if (connAck == null)
            {
                throw new MqttException($"No connack");
            }

            if (connAck.Status != MqttConnectionStatus.Accepted)
            {
                throw new MqttConnectException(connAck.Status, connAck.Status.ToString());
            }


            return connAck.SessionPresent ? SessionState.SessionPresent : SessionState.CleanSession;
        }

        public Task DisconnectAsync()
        {
            throw new NotImplementedException();
        }

        public async Task SubscribeAsync(string topicFilter, MqttQos qos)
        {
            var packetId = _packetIdProvider.GetPacketId();
            var subscribe = new SubscribePacket(packetId, new[] { new Subscription(topicFilter, qos) });
            var payload = GetPacket(serverName, NodeType.Server, subscribe.PacketToBytes(), "MQTT Subscribe");
            var scope = NetworkGraph.Monitoring.WithBeginScope(ref payload!, $"Subscribe {Name} to {payload.To} using topic {topicFilter}");
            NetworkGraph.Monitoring.Push(payload, MonitoringType.Connect, $"MQTT Client {ClientId} subscribes to broker {payload.To} using topic {topicFilter}");
            var subscribeTimeout = TimeSpan.FromSeconds(60);

            var response = await SendWithResponseAsync(payload);
            var subscribeAck = response.Payload.BytesToPacket<SubscribeAckPacket>();

            if (subscribeAck == null)
            {
                throw new MqttClientException($"Subscription Disconnected: {Id}, {topicFilter}");
            }

            if (subscribeAck.ReturnCodes.FirstOrDefault() == SubscribeReturnCode.Failure)
            {
                throw new MqttClientException($"Subscription Rejected: {Id}, {topicFilter}");
            }

            var session = _sessionRepository.Read(ClientId);

            if (session == null)
            {
                return;
            }

            session.Subscriptions.Add(new ClientSubscription() { 
                ClientId = ClientId, 
                MaximumQualityOfService = qos,
                TopicFilter = topicFilter});

            NetworkGraph.Monitoring.WithEndScope(ref response!);
        }

        public async Task<bool> PublishAsync(string topic, byte[] payload, MqttQos qos, bool retain = false)
        {
            ushort? packetId = qos == MqttQos.AtMostOnce ? null : (ushort?)_packetIdProvider.GetPacketId();
            var publish = new PublishPacket(topic, qos, retain, duplicated: false, packetId: packetId)
            {
                Payload = payload
            };

            var packetPayload = GetPacket(serverName, NodeType.Server, publish.PacketToBytes(), "MQTT Publish");
            var scope = NetworkGraph.Monitoring.WithBeginScope(ref packetPayload!,
                $"Publish {Name} to {packetPayload.To} with topic {topic}");

            if (!IsConnected)
            {
                SaveMessage(publish, ClientId, PendingMessageStatus.PendingToSend);
                return true;
            }

            if (qos > MqttQos.AtMostOnce)
            {
                SaveMessage(publish, ClientId, PendingMessageStatus.PendingToAcknowledge);
            }

            await SendAsync(packetPayload);
            //NetworkGraph.Monitoring.TryEndScope(scope);
            return true;
        }

        public Task<bool> UnsubscribeAsync(string topicFilter)
        {
            throw new NotImplementedException();
        }

        protected override async Task OnReceivedAsync(Packet packet)
        {
            var payload = packet.Payload.BytesToPacket<PacketBase>();
            if (payload == null)
            {
                await base.OnReceivedAsync(packet);
            }

            NetworkGraph.Monitoring.WithEndScope(ref packet);
            switch (payload!.Type)
            {
                case MqttPacketType.PublishAck:
                    await ProcessPublishAckFromBroker(packet, packet.Payload.BytesToPacket<PublishAckPacket>());
                    break;
                case MqttPacketType.PublishReceived:
                    break;
                case MqttPacketType.PublishRelease:
                    break;
                case MqttPacketType.PingResponse:
                    break;
                case MqttPacketType.Publish:
                    await ProcessPublishFromBroker(packet, packet.Payload.BytesToPacket<PublishPacket>());
                    break;
                default:
                    break;
            }
        }

        private async Task ProcessPublishFromBroker(Packet packet, PublishPacket? publishPacket)
        {
            if (publishPacket == null)
            {
                return;
            }
            NetworkGraph.Monitoring.Push(packet, MonitoringType.Receive, 
                $"MQTT Client {ClientId} receives Publish message from {packet.From} broker by topic {publishPacket.Topic}");

            NetworkGraph.Monitoring.WithEndScope(ref packet);
            await SendPublishAck(packet, ClientId, publishPacket.QualityOfService, publishPacket);

            MessageReceived?.Invoke(this,
                new MqttMessage(publishPacket.Topic, publishPacket.Payload, publishPacket.QualityOfService, packet.From));
        }

        private async Task SendPublishAck(Packet packet, string clientId, MqttQos qos, PublishPacket publishPacket)
        {
            var ack = new PublishAckPacket(publishPacket.PacketId ?? 0);
            var ackPayload = ack.PacketToBytes() ?? Array.Empty<byte>();

            var reversePacket = NetworkGraph.GetReversePacket(packet, ackPayload.ToArray(), "MQTT PubAck");
            await SendAsync(reversePacket);
        }


        private Task ProcessPublishAckFromBroker(Packet payload, PublishAckPacket? publishAckPacket)
        {
            if (publishAckPacket == null)
            {
                return Task.CompletedTask;
            }

            var session = _sessionRepository.Read(ClientId);

            if (session == null)
            {
                throw new MqttException($"Client Session {ClientId} Not Found");
            }

            var pendingMessage = session
                .GetPendingMessages()
                .FirstOrDefault(p => p.PacketId.HasValue && p.PacketId.Value == publishAckPacket.PacketId);

            session.RemovePendingMessage(pendingMessage);

            _sessionRepository.Update(session);

            NetworkGraph.Monitoring.WithEndScope(ref payload);

            return Task.CompletedTask;
        }

        private void OpenClientSession(bool cleanSession)
        {
            var session = string.IsNullOrEmpty(ClientId) ? default(ClientSession) : _sessionRepository.Read(ClientId);

            if (cleanSession && session != null)
            {
                _sessionRepository.Delete(session.Id);
                session = null;
            }

            if (session == null)
            {
                session = new ClientSession(ClientId, cleanSession);

                _sessionRepository.Create(session);
            }
        }

        private void RemovePendingAcknowledgement(string clientId, ushort packetId, MqttPacketType type)
        {
            var session = _sessionRepository.Read(clientId);

            if (session == null)
            {
                throw new MqttException(string.Format($"Client Session {clientId} Not Found", clientId));
            }

            var pendingAcknowledgement = session
                .GetPendingAcknowledgements()
                .FirstOrDefault(u => u.Type == type && u.PacketId == packetId);

            session.RemovePendingAcknowledgement(pendingAcknowledgement);

            _sessionRepository.Update(session);
        }

        private void RemovePendingMessage(string clientId, ushort packetId)
        {
            var session = _sessionRepository.Read(clientId);

            if (session == null)
            {
                throw new MqttException(string.Format($"Client Session {clientId} Not Found", clientId));
            }

            var pendingMessage = session
                .GetPendingMessages()
                .FirstOrDefault(p => p.PacketId.HasValue && p.PacketId.Value == packetId);

            session.RemovePendingMessage(pendingMessage);

            _sessionRepository.Update(session);
        }

        private void SaveMessage(PublishPacket message, string clientId, PendingMessageStatus status)
        {
            if (message.QualityOfService == MqttQos.AtMostOnce)
            {
                return;
            }

            var session = _sessionRepository.Read(clientId);

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

            _sessionRepository.Update(session);
        }
    }
}
