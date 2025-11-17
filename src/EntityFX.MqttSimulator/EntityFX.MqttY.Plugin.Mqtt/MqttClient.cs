using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Counter;
using EntityFX.MqttY.Plugin.Mqtt.Internals;

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
            string? clientId, TicksOptions ticksOptions)
            : base(index, name, address, protocolType, specification, 
                ticksOptions)
        {
            this._packetManager = packetManager;
            ClientId = clientId ?? name;

            _mqttCounters = new MqttCounters("Mqtt", ticksOptions);
            counters.AddCounter(_mqttCounters);
        }

        public string ClientId { get; set; }

        public string Server => ServerName;

        public SessionState Connect(string server, bool cleanSession = false)
        {
            var connect = new ConnectPacket(ClientId, true);
            var connectId = Guid.NewGuid();
            var payload = GetPacket(connectId, server, NodeType.Server, 
                _packetManager.PacketToBytes(connect), ProtocolType, "MQTT Connect", willWait: true);

            if (IsConnected)
            {
                return !cleanSession ? SessionState.SessionPresent : SessionState.CleanSession;
            }

            var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref payload!, 
                $"MQTT Client {ClientId} connects to broker {server}");

            NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, payload, NetworkLoggerType.Connect, 
                $"MQTT Client {ClientId} connects to broker {server}", ProtocolType, "MQTT Connect");
            _mqttCounters.PacketTypeCounters[connect.Type].Increment();

            var response = ConnectImplementation(server, payload);

            if (response == null)
            {
                NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref payload);
                throw new MqttException($"Unable connect to broker {server}");
            }

            var networkPacket = response!;
            NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref networkPacket);

            OpenClientSession(cleanSession);


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

            _mqttCounters.PacketTypeCounters[connAck.Type].Increment();

            return connAck.SessionPresent ? SessionState.SessionPresent : SessionState.CleanSession;
        }

        public bool BeginConnect(string server, bool cleanSession = false)
        {
            var connect = new ConnectPacket(ClientId, true);
            var connectId = Guid.NewGuid();
            var payload = GetContextPacket<(string Server, bool CleanSession)>(connectId, server, NodeType.Server,
                _packetManager.PacketToBytes(connect), ProtocolType, new (server, cleanSession), "MQTT Connect", willWait: true);

            if (IsConnected)
            {
                return true;
            }

            var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref payload!,
                $"MQTT Client {ClientId} connects to broker {server}");

            NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, payload, NetworkLoggerType.Connect,
                $"MQTT Client {ClientId} connects to broker {server}", ProtocolType, "MQTT Connect");
            _mqttCounters.PacketTypeCounters[connect.Type].Increment();

            var result = BeginConnectImplementation(server, payload);

            return result;
        }

        public SessionState CompleteConnect(NetworkPacket? response, string server, bool cleanSession = false)
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
            var networkPacket = (NetworkPacket)response!;


            NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref networkPacket);

            OpenClientSession(cleanSession);

            var connAck = _packetManager.BytesToPacket<ConnectAckPacket>(networkPacket.Payload);

            if (connAck == null)
            {
                throw new MqttException($"No connack");
            }

            if (connAck.Status != MqttConnectionStatus.Accepted)
            {
                throw new MqttConnectException(connAck.Status, connAck.Status.ToString());
            }

            _mqttCounters.PacketTypeCounters[connAck.Type].Increment();

            CompleteConnectImplementation(response);

            return connAck.SessionPresent ? SessionState.SessionPresent : SessionState.CleanSession;
        }

        //public override bool Disconnect()
        //{
        //    base.Disconnect();
        //}

        public void Subscribe(string topicFilter, MqttQos qos)
        {
            var packetId = _packetIdProvider.GetPacketId();
            var subscribe = new SubscribePacket(packetId, new[] { new Subscription(topicFilter, qos) });
            var subscribeId = Guid.NewGuid();
            var payload = GetPacket(subscribeId, ServerName, NodeType.Server, _packetManager.PacketToBytes(subscribe), ProtocolType, "MQTT Subscribe");
            var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref payload!, 
                $"Subscribe {Name} to {payload.To} using topic {topicFilter}");

            NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, payload, NetworkLoggerType.Send, 
                $"MQTT Client {ClientId} subscribes to broker {payload.To} using topic {topicFilter}", ProtocolType, "MQTT Subscribe");

            var subscribeTimeout = TimeSpan.FromSeconds(60);

            _mqttCounters.PacketTypeCounters[subscribe.Type].Increment();

            var sendResult = Send(payload);

            if (!sendResult)
            {
                _mqttCounters.Refuse(subscribe.Type);
                return;
            }

            //TODO: get response
            var response = WaitResponse(subscribeId);

            if (response == null)
            {
                //No Subscribe Ack (timeout)
                return;
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

            var session = _sessionRepository.Read(ClientId);

            _mqttCounters.Increment(subscribeAck.Type);

            if (session == null)
            {
                return;
            }

            session.Subscriptions.Add(new ClientSubscription() { 
                ClientId = ClientId, 
                MaximumQualityOfService = qos,
                TopicFilter = topicFilter});


            NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref responsePacket!);
        }

        public bool Publish(string topic, byte[] payload, MqttQos qos, bool retain = false)
        {
            ushort? packetId = qos == MqttQos.AtMostOnce ? null : (ushort?)_packetIdProvider.GetPacketId();
            var publish = new PublishPacket(topic, qos, retain, duplicated: false, packetId: packetId)
            {
                Payload = payload
            };

            var packetPayload = GetPacket(Guid.NewGuid(), ServerName, NodeType.Server, 
                _packetManager.PacketToBytes(publish), ProtocolType, "MQTT Publish");
            var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref packetPayload!,
                $"Publish {Name} to {packetPayload.To} with topic {topic}");

            NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, packetPayload, NetworkLoggerType.Send, 
                $"MQTT Client {ClientId} publishes to broker {packetPayload.To} using topic {topic}", ProtocolType, "MQTT Publish");

            if (!IsConnected)
            {
                SaveMessage(publish, ClientId, PendingMessageStatus.PendingToSend);
                return true;
            }

            if (qos > MqttQos.AtMostOnce)
            {
                SaveMessage(publish, ClientId, PendingMessageStatus.PendingToAcknowledge);
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

        public void CompleteUnsubscribe(NetworkPacket? response, string topicFilter)
        {
            throw new NotImplementedException();
        }

        protected override void OnReceived(NetworkPacket packet)
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
                    break;
                case MqttPacketType.PublishReceived:
                    break;
                case MqttPacketType.PublishRelease:
                    break;
                case MqttPacketType.PingResponse:
                    break;
                case MqttPacketType.Publish:
                    ProcessPublishFromBroker(packet, _packetManager.BytesToPacket<PublishPacket>(packet.Payload));
                    break;
                case MqttPacketType.ConnectAck:
                    ProcessConnectAckFromBroker(packet, _packetManager.BytesToPacket<ConnectAckPacket>(packet.Payload));
                    break;
                default:
                    return;
            }

            _mqttCounters.PacketTypeCounters[payload.Type].Increment();
        }

        private void ProcessConnectAckFromBroker(NetworkPacket packet, ConnectAckPacket? connectAckPacket)
        {
            var responsePacket = WaitResponse(packet.RequestId!.Value);

            var contextPacket = responsePacket?.Packet as NetworkPacket<(string Server, bool CleanSession)>;
            CompleteConnect(packet, contextPacket?.TypedContext.Server ?? string.Empty, contextPacket?.TypedContext.CleanSession ?? false);
        }

        private void ProcessPublishFromBroker(NetworkPacket packet, PublishPacket? publishPacket)
        {
            if (publishPacket == null)
            {
                return;
            }

            if (Name == "mgx11")
            {

            }

            NetworkSimulator!.Monitoring.Push(NetworkSimulator.TotalTicks, packet, NetworkLoggerType.Receive, 
                $"MQTT Client {ClientId} receives Publish message from {packet.From} broker by topic {publishPacket.Topic}", 
                ProtocolType, "MQTT Publish");

            NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);
            SendPublishAck(packet, ClientId, publishPacket.QualityOfService, publishPacket);

            MessageReceived?.Invoke(this,
                new MqttMessage(publishPacket.Topic, publishPacket.Payload, publishPacket.QualityOfService, packet.From));
        }

        private void SendPublishAck(NetworkPacket packet, string clientId, MqttQos qos, PublishPacket publishPacket)
        {
            var ack = new PublishAckPacket(publishPacket.PacketId ?? 0);
            var ackPayload = _packetManager.PacketToBytes(ack) ?? Array.Empty<byte>();

            var reversePacket = NetworkSimulator!.GetReversePacket(packet, ackPayload.ToArray(), "MQTT PubAck");
            NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, packet, NetworkLoggerType.Send,
                $"Send MQTT publish ack {packet.From} to {packet.To} with {publishPacket.Topic} (QoS={publishPacket.QualityOfService})",
                ProtocolType, "MQTT PubAck");

            Send(reversePacket);
        }


        private void ProcessPublishAckFromBroker(NetworkPacket payload, PublishAckPacket? publishAckPacket)
        {
            if (publishAckPacket == null)
            {
                return;
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

            NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref payload);
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

        public bool BeginSubscribe(string topicFilter, MqttQos qos)
        {
            var packetId = _packetIdProvider.GetPacketId();
            var subscribe = new SubscribePacket(packetId, new[] { new Subscription(topicFilter, qos) });
            var subscribeId = Guid.NewGuid();
            var payload = GetPacket(subscribeId, ServerName, NodeType.Server, _packetManager.PacketToBytes(subscribe), ProtocolType, "MQTT Subscribe");
            var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref payload!,
                $"Subscribe {Name} to {payload.To} using topic {topicFilter}");

            NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, payload, NetworkLoggerType.Send,
                $"MQTT Client {ClientId} subscribes to broker {payload.To} using topic {topicFilter}", ProtocolType, "MQTT Subscribe");

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

        public void CompleteSubscribe(NetworkPacket? response, string topicFilter, MqttQos qos)
        {
            if (response == null)
            {
                //No Subscribe Ack (timeout)
                return;
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

            var session = _sessionRepository.Read(ClientId);

            _mqttCounters.Increment(subscribeAck.Type);

            if (session == null)
            {
                return;
            }

            session.Subscriptions.Add(new ClientSubscription()
            {
                ClientId = ClientId,
                MaximumQualityOfService = qos,
                TopicFilter = topicFilter
            });


            NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref response!);
        }
    }
}
