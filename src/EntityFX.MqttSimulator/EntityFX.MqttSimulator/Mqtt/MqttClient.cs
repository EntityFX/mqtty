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

namespace EntityFX.MqttY.Mqtt
{

    internal class MqttClient : Client, IMqttClient
    {
        private readonly PacketIdProvider _packetIdProvider = new();

        private readonly IRepository<ClientSession> _sessionRepository 
            = new InMemoryRepository<ClientSession> ();

        private IDictionary<MqttPacketType, Func<string, ushort, IPacket?>> _senderRules;


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

            NetworkGraph.Monitoring.TryBeginScope(ref payload!, $"Connect {Name} to {server}");

            if (!IsConnected)
            {
                var result = Connect(server);
                if (!result)
                {
                    throw new MqttException($"Unable to server {server}");
                }
            }

            OpenClientSession(cleanSession);


            var response = await SendWithResponseAsync(payload);

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

            NetworkGraph.Monitoring.TryEndScope(ref response);

            return connAck.SessionPresent ? SessionState.SessionPresent : SessionState.CleanSession; ;
        }

        public Task DisconnectAsync()
        {
            throw new NotImplementedException();
        }

        public async Task<bool> PublishAsync(string topic, byte[] payload, MqttQos qos, bool retain = false)
        {
            ushort? packetId = qos == MqttQos.AtMostOnce ? null : (ushort?)_packetIdProvider.GetPacketId();
            var publish = new PublishPacket(topic, qos, retain, duplicated: false, packetId: packetId)
            {
                Payload = payload
            };

            var packetPayload = GetPacket(serverName, NodeType.Server, publish.PacketToBytes(), "MQTT Publish");
            NetworkGraph.Monitoring.TryBeginScope(ref packetPayload!, $"Publish {Name} to {packetPayload.To} with topic {topic}");

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

            return true;
        }

        public async Task SubscribeAsync(string topicFilter, MqttQos qos)
        {
            var packetId = _packetIdProvider.GetPacketId();
            var subscribe = new SubscribePacket(packetId, new[] { new Subscription(topicFilter, qos) });
            var payload = GetPacket(serverName, NodeType.Server, subscribe.PacketToBytes(), "MQTT Subscribe");
            NetworkGraph.Monitoring.TryBeginScope(ref payload!, $"Subscribe {Name} to {payload.To} with topic {topicFilter}");
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

            NetworkGraph.Monitoring.TryEndScope(ref response);
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

            switch (payload!.Type)
            {
                case MqttPacketType.PublishAck:
                    await ProcessAckPacket(packet, packet.Payload.BytesToPacket<PublishPacket>());
                    break;
                case MqttPacketType.PublishReceived:
                    break;
                case MqttPacketType.PublishRelease:
                    break;
                case MqttPacketType.PingResponse:
                    break;

                case MqttPacketType.Publish:
                    await ProcessPublish(packet, packet.Payload.BytesToPacket<PublishPacket>());
                    break;


                default:
                    break;
            }
        }

        private async Task ProcessPublish(Packet packet, PublishPacket? publishPacket)
        {

        }

        private Task ProcessAckPacket(Packet payload, PublishPacket? publishPacket)
        {
            if (publishPacket == null)
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
                .FirstOrDefault(p => p.PacketId.HasValue && p.PacketId.Value == publishPacket.PacketId);

            session.RemovePendingMessage(pendingMessage);

            _sessionRepository.Update(session);

            NetworkGraph.Monitoring.TryEndScope(ref payload);

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

        protected override void BeforeSend(Packet packet)
        {
        }

        protected override void AfterSend(Packet packet)
        {
        }

        protected override void BeforeReceive(Packet packet)
        {
        }
    }
}
