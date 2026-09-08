using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Counter;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using System.Collections.Immutable;

namespace EntityFX.MqttY.Plugin.Mqtt
{
    public class MqttClient : Client, IMqttClient
    {
        private readonly PacketIdProvider _packetIdProvider = new();

        private readonly IRepository<ClientSession> _sessionRepository
            = new InMemoryRepository<ClientSession>();
        private readonly IMqttPacketManager _packetManager;

        public event EventHandler<MqttMessage>? MessageReceived;

        private readonly MqttCounters _mqttCounters;

        public MqttClient(IMqttPacketManager packetManager,
            int index, string name, string address, string protocolType,
            string specification,
            string? clientId, TicksOptions ticksOptions, bool enableCounters)
            : base(index, name, address, protocolType, specification,
                ticksOptions, enableCounters)
        {
            this._packetManager = packetManager;
            ClientId = clientId ?? name;

            _mqttCounters = new MqttCounters(Name, Name.Substring(0, 2), "MqttClient", "MC", ticksOptions, enableCounters);
            counters.AddCounter(_mqttCounters);
        }

        public string ClientId { get; set; }

        public SessionState? CurrentSessionState { get; private set; }

        public string Server => ServerName ?? string.Empty;

        public override bool IsQuiescent => base.IsQuiescent &&
            _sessionRepository.ReadAll().All(session =>
                !session.GetPendingMessages().Any() &&
                !session.GetPendingAcknowledgements().Any(ack => ack.Type != MqttPacketType.PublishComplete));

        public IReadOnlyDictionary<string, MqttSubscribtion[]> Subscribtions => 
            _sessionRepository.ReadAll().ToDictionary(s => s.Id, s => s.Subscriptions.Select(cs => new MqttSubscribtion(cs.TopicFilter, cs.MaximumQualityOfService)).ToArray()).ToImmutableDictionary();

        public bool IsSubscribed(string topicFilter)
        {
            if (string.IsNullOrEmpty(topicFilter)) return false;
            var session = _sessionRepository.Read(Name);
            return session?.GetSubscriptions().Any(subscription => subscription.TopicFilter == topicFilter) == true;
        }

        public SessionState Connect(string server, bool cleanSession = false)
        {
            var connect = new ConnectPacket(ClientId, cleanSession);
            var connectId = NetworkSimulator!.GetPacketId();
            var payload = GetContextPacket<(string Server, bool CleanSession)>(
                connectId, server, NodeType.Server, ServerIndex ?? -1,
                _packetManager.PacketToBytes(connect), ProtocolType, new(server, cleanSession),
                "MQTT Connect", outgoingTicks: TicksOptions.OutgoingWaitTicks);

            if (IsConnected)
            {
                return CurrentSessionState ?? SessionState.CleanSession;
            }

            _mqttCounters.PacketTypeCounters[connect.Type].Increment();

            var response = ConnectImplementation(server, payload);

            if (response == null)
            {
                throw new MqttException($"Unable connect to broker {server}");
            }

            var networkPacket = response!;

            if (response == null)
            {
                throw new MqttException($"No connack");
            }

            var connAck = _packetManager.BytesToPacket<ConnectAckPacket>(response.Payload);

            if (connAck == null)
            {
                throw new MqttException($"No connack");
            }

            if (connAck.Status != MqttConnectionStatus.Accepted)
            {
                throw new MqttConnectException(connAck.Status, connAck.Status.ToString());
            }

            OpenClientSession(cleanSession, connAck.SessionPresent);

            _mqttCounters.PacketTypeCounters[connAck.Type].Increment();

            CurrentSessionState = connAck.SessionPresent
                ? SessionState.SessionPresent
                : SessionState.CleanSession;
            return CurrentSessionState.Value;
        }

        public bool BeginConnect(string server, bool cleanSession = false)
        {
            var connect = new ConnectPacket(ClientId, cleanSession);
            var connectId = NetworkSimulator!.GetPacketId();
            var payload = GetContextPacket<(string Server, bool CleanSession)>(
                connectId, server, NodeType.Server, ServerIndex ?? -1,
                _packetManager.PacketToBytes(connect), ProtocolType, new(server, cleanSession),
                "MQTT Connect", outgoingTicks: TicksOptions.OutgoingWaitTicks);

            if (IsConnected)
            {
                return true;
            }

            CurrentSessionState = null;

            _mqttCounters.PacketTypeCounters[connect.Type].Increment();

            var result = BeginConnectImplementation(server, payload);

            return result;
        }

        public SessionState CompleteConnect(INetworkPacket? response, string server, bool cleanSession = false)
        {
            //if (response == null)
            //{
            //    NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref null);
            //    throw new MqttException($"Unable connect to broker {server}");
            //}

            if (response == null)
            {
                throw new MqttException($"No connack");
            }
            var networkPacket = (INetworkPacket)response!;


            NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref networkPacket);

            var connAck = _packetManager.BytesToPacket<ConnectAckPacket>(networkPacket.Payload);

            if (connAck == null)
            {
                throw new MqttException($"No connack");
            }

            if (connAck.Status != MqttConnectionStatus.Accepted)
            {
                throw new MqttConnectException(connAck.Status, connAck.Status.ToString());
            }

            OpenClientSession(cleanSession, connAck.SessionPresent);

            _mqttCounters.PacketTypeCounters[connAck.Type].Increment();

            CompleteConnectImplementation(response);

            CurrentSessionState = connAck.SessionPresent
                ? SessionState.SessionPresent
                : SessionState.CleanSession;
            return CurrentSessionState.Value;
        }

        //public override bool Disconnect()
        //{
        //    base.Disconnect();
        //}

        public void Subscribe(string topicFilter, MqttQos qos)
        {
            if (!IsConnected)
            {
                throw new MqttClientException($"Client {Id} is not connected");
            }

            var packetId = _packetIdProvider.GetPacketId();
            var subscribe = new SubscribePacket(packetId, new[] { new Subscription(topicFilter, qos) });
            var subscribeId = NetworkSimulator!.GetPacketId();

            var payload = GetContextPacket<(string TopicFilter, MqttQos Qos)>(
                subscribeId, ServerName ?? string.Empty, NodeType.Server, ServerIndex ?? -1,
                _packetManager.PacketToBytes(subscribe), ProtocolType, new(topicFilter, qos),
                "MQTT Subscribe", outgoingTicks: TicksOptions.OutgoingWaitTicks);


            var subscribeTimeout = TimeSpan.FromSeconds(60);

            _mqttCounters.PacketTypeCounters[subscribe.Type].Increment();

            var sendResult = Send(payload);

            if (!sendResult)
            {
                _mqttCounters.Refuse(subscribe.Type);
                throw new MqttClientException($"Unable to send subscription: {Id}, {topicFilter}");
            }

            //TODO: get response
            var response = WaitResponse(subscribeId);

            if (response == null)
            {
                throw new MqttClientException($"Subscription timed out: {Id}, {topicFilter}");
            }

            var responsePacket = response.Packet;

            var subscribeAck = _packetManager.BytesToPacket<SubscribeAckPacket>(responsePacket.Payload);

            if (subscribeAck == null)
            {
                throw new MqttClientException($"Subscription Disconnected: {Id}, {topicFilter}");
            }

            if (subscribeAck.ReturnCodes.FirstOrDefault() == SubscribeReturnCode.Failure)
            {
                throw new MqttClientException($"Subscription Rejected: {Id}, {topicFilter}");
            }

            var session = _sessionRepository.Read(Name);

            _mqttCounters.Increment(subscribeAck.Type);

            if (session == null)
            {
                throw new MqttClientException($"Client session not found: {Id}");
            }

            UpsertSubscription(session, topicFilter, qos);
            _sessionRepository.Update(session);
        }

        public bool Publish(string topic, byte[] payload, MqttQos qos, bool retain = false)
        {
            ushort? packetId = qos == MqttQos.AtMostOnce ? null : (ushort?)_packetIdProvider.GetPacketId();
            var publish = new PublishPacket(topic, qos, retain, duplicated: false, packetId: packetId)
            {
                Payload = payload
            };

            var packetPayload = GetPacket(NetworkSimulator!.GetPacketId(), ServerName ?? string.Empty,
                NodeType.Server,
                ServerIndex ?? -1,
                _packetManager.PacketToBytes(publish), ProtocolType, "MQTT Publish");
            var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref packetPayload!,
                $"Publish {Name} to {packetPayload.To} with topic {topic}");

            if (!IsConnected)
            {
                SaveMessage(publish, Name, PendingMessageStatus.PendingToSend);
                return true;
            }

            if (qos > MqttQos.AtMostOnce)
            {
                var status = qos == MqttQos.ExactlyOnce
                    ? PendingMessageStatus.AwaitingPublishReceived
                    : PendingMessageStatus.PendingToAcknowledge;
                SaveMessage(publish, Name, status);
            }
            _mqttCounters.Increment(publish.Type);

            var sendResult = Send(packetPayload);

            if (!sendResult)
            {
                _mqttCounters.Refuse(publish.Type);
            }

            return sendResult;
        }

        public bool Unsubscribe(string topicFilter)
        {
            throw new NotImplementedException();
        }

        public bool BeginUnsubscribe(string topicFilter)
        {
            throw new NotImplementedException();
        }

        public void CompleteUnsubscribe(INetworkPacket? response, string topicFilter)
        {
            throw new NotImplementedException();
        }

        protected override void OnReceived(INetworkPacket packet)
        {
            var payload = _packetManager.BytesToPacket<PacketBase>(packet.Payload);
            if (payload == null)
            {
                base.OnReceived(packet);
            }

            NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);
            switch (payload!.Type)
            {
                case MqttPacketType.PublishAck:
                    ProcessPublishAckFromBroker(packet, _packetManager.BytesToPacket<PublishAckPacket>(packet.Payload));
                    return;
                case MqttPacketType.PublishReceived:
                    ProcessPublishReceivedFromBroker(packet,
                        _packetManager.BytesToPacket<PublishReceivedPacket>(packet.Payload));
                    return;
                case MqttPacketType.PublishRelease:
                    ProcessPublishReleaseFromBroker(packet,
                        _packetManager.BytesToPacket<PublishReleasePacket>(packet.Payload));
                    return;
                case MqttPacketType.PublishComplete:
                    ProcessPublishCompleteFromBroker(packet,
                        _packetManager.BytesToPacket<PublishCompletePacket>(packet.Payload));
                    return;
                case MqttPacketType.PingResponse:
                    break;
                case MqttPacketType.Publish:
                    ProcessPublishFromBroker(packet, _packetManager.BytesToPacket<PublishPacket>(packet.Payload));
                    return;
                case MqttPacketType.ConnectAck:
                    ProcessConnectAckFromBroker(packet, _packetManager.BytesToPacket<ConnectAckPacket>(packet.Payload));
                    return;
                case MqttPacketType.SubscribeAck:
                    ProcessSubscribeAckFromBroker(packet, _packetManager.BytesToPacket<SubscribeAckPacket>(packet.Payload));
                    return;
                default:
                    return;
            }

            _mqttCounters.PacketTypeCounters[payload.Type].Increment();
        }

        private void ProcessSubscribeAckFromBroker(INetworkPacket packet, SubscribeAckPacket? bytesToPacket)
        {
            var contextPacket = (NetworkPacket<(string TopicFilter, MqttQos Qos)>)packet;
            CompleteSubscribe(packet, contextPacket.TypedContext.TopicFilter ?? string.Empty,
                contextPacket.TypedContext.Qos);
        }

        private void ProcessConnectAckFromBroker(INetworkPacket packet, ConnectAckPacket? connectAckPacket)
        {
            var contextPacket = (NetworkPacket<(string Server, bool CleanSession)>)packet;
            CompleteConnect(packet, contextPacket.TypedContext.Server ?? string.Empty, contextPacket.TypedContext.CleanSession);
        }

        private void ProcessPublishFromBroker(INetworkPacket packet, PublishPacket? publishPacket)
        {
            if (publishPacket == null)
            {
                return;
            }

            if (publishPacket.QualityOfService == MqttQos.ExactlyOnce)
            {
                var session = GetSession();
                var pendingMessage = session.GetPendingMessages().FirstOrDefault(message =>
                    message.PacketId == publishPacket.PacketId &&
                    message.Status == PendingMessageStatus.AwaitingPublishRelease);
                if (pendingMessage == null)
                {
                    SaveMessage(publishPacket, Name, PendingMessageStatus.AwaitingPublishRelease, packet.Id);
                }

                SendPublishReceived(packet, publishPacket.PacketId ?? 0);
                return;
            }

            if (publishPacket.QualityOfService == MqttQos.AtLeastOnce)
            {
                SendPublishAck(packet, ClientId, publishPacket.QualityOfService, publishPacket);
            }

            MessageReceived?.Invoke(this,
                new MqttMessage(publishPacket.Topic, publishPacket.Payload, publishPacket.QualityOfService, packet.From));
            CompleteDelivery(packet.From, packet.Id);
        }

        private void SendPublishAck(INetworkPacket packet, string clientId, MqttQos qos, PublishPacket publishPacket)
        {
            var ack = new PublishAckPacket(publishPacket.PacketId ?? 0);
            var ackPayload = _packetManager.PacketToBytes(ack) ?? Array.Empty<byte>();

            var reversePacket = NetworkSimulator!.GetReversePacket(packet, ackPayload.ToArray(), "MQTT PubAck");

            Send(reversePacket);
        }

        private void SendPublishReceived(INetworkPacket packet, ushort packetId)
        {
            var received = new PublishReceivedPacket(packetId);
            var payload = _packetManager.PacketToBytes(received) ?? Array.Empty<byte>();
            var reversePacket = NetworkSimulator!.GetReversePacket(packet, payload, "MQTT PubRec");
            Send(reversePacket);
            _mqttCounters.PacketTypeCounters[received.Type].Increment();
        }

        private void ProcessPublishReceivedFromBroker(
            INetworkPacket packet, PublishReceivedPacket? publishReceivedPacket)
        {
            if (publishReceivedPacket == null)
            {
                return;
            }

            var session = GetSession();
            var pendingMessage = session.GetPendingMessages().FirstOrDefault(message =>
                message.PacketId == publishReceivedPacket.PacketId &&
                (message.Status == PendingMessageStatus.AwaitingPublishReceived ||
                 message.Status == PendingMessageStatus.AwaitingPublishComplete));
            if (pendingMessage == null)
            {
                return;
            }

            pendingMessage.Status = PendingMessageStatus.AwaitingPublishComplete;
            _sessionRepository.Update(session);

            var release = new PublishReleasePacket(publishReceivedPacket.PacketId);
            var payload = _packetManager.PacketToBytes(release) ?? Array.Empty<byte>();
            var reversePacket = NetworkSimulator!.GetReversePacket(packet, payload, "MQTT PubRel");
            Send(reversePacket);
            _mqttCounters.PacketTypeCounters[release.Type].Increment();
        }

        private void ProcessPublishReleaseFromBroker(
            INetworkPacket packet, PublishReleasePacket? publishReleasePacket)
        {
            if (publishReleasePacket == null)
            {
                return;
            }

            var session = GetSession();
            var pendingMessage = session.GetPendingMessages().FirstOrDefault(message =>
                message.PacketId == publishReleasePacket.PacketId &&
                message.Status == PendingMessageStatus.AwaitingPublishRelease);

            if (pendingMessage != null)
            {
                session.RemovePendingMessage(pendingMessage);
                session.AddPendingAcknowledgement(new PendingAcknowledgement
                {
                    Type = MqttPacketType.PublishComplete,
                    PacketId = publishReleasePacket.PacketId
                });
                _sessionRepository.Update(session);

                MessageReceived?.Invoke(this,
                    new MqttMessage(pendingMessage.Topic, pendingMessage.Payload,
                        pendingMessage.QualityOfService, packet.From));
                if (pendingMessage.CorrelationId.HasValue)
                    CompleteDelivery(packet.From, pendingMessage.CorrelationId.Value);
            }

            var complete = new PublishCompletePacket(publishReleasePacket.PacketId);
            var payload = _packetManager.PacketToBytes(complete) ?? Array.Empty<byte>();
            var reversePacket = NetworkSimulator!.GetReversePacket(packet, payload, "MQTT PubComp");
            Send(reversePacket);
            _mqttCounters.PacketTypeCounters[complete.Type].Increment();
        }

        private void ProcessPublishCompleteFromBroker(
            INetworkPacket packet, PublishCompletePacket? publishCompletePacket)
        {
            if (publishCompletePacket == null)
            {
                return;
            }

            var session = GetSession();
            var pendingMessage = session.GetPendingMessages().FirstOrDefault(message =>
                message.PacketId == publishCompletePacket.PacketId &&
                message.Status == PendingMessageStatus.AwaitingPublishComplete);
            session.RemovePendingMessage(pendingMessage);
            _sessionRepository.Update(session);
            CompletePublisher(packet.From, MqttQos.ExactlyOnce,
                publishCompletePacket.PacketId, NetworkSimulator!.TotalTicks);
        }

        private ClientSession GetSession()
        {
            return _sessionRepository.Read(Name)
                ?? throw new MqttException($"Client Session {ClientId} Not Found");
        }


        private void ProcessPublishAckFromBroker(INetworkPacket payload, PublishAckPacket? publishAckPacket)
        {
            if (publishAckPacket == null)
            {
                return;
            }

            var session = _sessionRepository.Read(Name);

            if (session == null)
            {
                throw new MqttException($"Client Session {ClientId} Not Found");
            }

            var pendingMessage = session
                .GetPendingMessages()
                .FirstOrDefault(p => p.PacketId.HasValue && p.PacketId.Value == publishAckPacket.PacketId);

            session.RemovePendingMessage(pendingMessage);

            _sessionRepository.Update(session);

            CompletePublisher(payload.From, MqttQos.AtLeastOnce,
                publishAckPacket.PacketId, NetworkSimulator!.TotalTicks);

            NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref payload);
        }

        private void OpenClientSession(bool cleanSession, bool sessionPresent)
        {
            var session = _sessionRepository.Read(Name);

            if ((cleanSession || !sessionPresent) && session != null)
            {
                _sessionRepository.Delete(session.Id);
                session = null;
            }

            if (session == null)
            {
                session = new ClientSession(Name, ClientId, cleanSession);

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

        private void SaveMessage(PublishPacket message, string clientId, PendingMessageStatus status,
            long? correlationId = null)
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
                Payload = message.Payload,
                CorrelationId = correlationId
            };

            session.AddPendingMessage(savedMessage);

            _sessionRepository.Update(session);
        }

        public bool BeginSubscribe(string topicFilter, MqttQos qos)
        {
            if (!IsConnected)
            {
                return false;
            }

            var packetId = _packetIdProvider.GetPacketId();
            var subscribe = new SubscribePacket(packetId, new[] { new Subscription(topicFilter, qos) });
            var subscribeId = NetworkSimulator!.GetPacketId();

            var payload = GetContextPacket<(string TopicFilter, MqttQos Qos)>(
                subscribeId, ServerName ?? string.Empty, NodeType.Server, ServerIndex ?? -1,
                _packetManager.PacketToBytes(subscribe), ProtocolType, new(topicFilter, qos),
                "MQTT Subscribe", outgoingTicks: TicksOptions.OutgoingWaitTicks);


            var subscribeTimeout = TimeSpan.FromSeconds(60);

            _mqttCounters.PacketTypeCounters[subscribe.Type].Increment();

            var sendResult = Send(payload);

            if (!sendResult)
            {
                _mqttCounters.Refuse(subscribe.Type);
                return false;
            }

            return true;
        }

        public void CompleteSubscribe(INetworkPacket? response, string topicFilter, MqttQos qos)
        {
            if (response == null)
            {
                throw new MqttClientException($"Subscription timed out: {Id}, {topicFilter}");
            }


            var subscribeAck = _packetManager.BytesToPacket<SubscribeAckPacket>(response.Payload);

            if (subscribeAck == null)
            {
                throw new MqttClientException($"Subscription Disconnected: {Id}, {topicFilter}");
            }

            if (subscribeAck.ReturnCodes.FirstOrDefault() == SubscribeReturnCode.Failure)
            {
                throw new MqttClientException($"Subscription Rejected: {Id}, {topicFilter}");
            }

            var session = _sessionRepository.Read(Name);

            _mqttCounters.Increment(subscribeAck.Type);

            if (session == null)
            {
                return;
            }

            UpsertSubscription(session, topicFilter, qos);

            _sessionRepository.Update(session);
        }

        private void CompletePublisher(string brokerName, MqttQos qos, ushort packetId, long tick)
        {
            if (NetworkSimulator!.GetNode(brokerName, NodeType.Server) is IMqttBrokerMetricsSink sink)
                sink.CompletePublisher(Name, qos, packetId, tick);
        }

        private void CompleteDelivery(string brokerName, long outgoingPublishPacketId)
        {
            if (NetworkSimulator!.GetNode(brokerName, NodeType.Server) is IMqttBrokerMetricsSink sink)
                sink.CompleteDelivery(outgoingPublishPacketId);
        }

        private void UpsertSubscription(ClientSession session, string topicFilter, MqttQos qos)
        {
            var subscription = session.GetSubscriptions()
                .FirstOrDefault(item => item.TopicFilter == topicFilter);
            if (subscription == null)
            {
                session.AddSubscription(new ClientSubscription
                {
                    ClientId = ClientId,
                    MaximumQualityOfService = qos,
                    TopicFilter = topicFilter
                });
            }
            else
            {
                subscription.MaximumQualityOfService = qos;
            }

        }

        public override void Clear()
        {
            _sessionRepository.Clear();
            base.Clear();
        }
    }
}
