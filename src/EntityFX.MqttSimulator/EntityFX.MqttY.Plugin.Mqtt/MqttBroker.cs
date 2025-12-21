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
    internal class MqttBroker : Server, IMqttBroker
    {
        private readonly IRepository<ClientSession> _sessionRepository = new InMemoryRepository<ClientSession>();

        private readonly MqttQos _maximumQualityOfService = MqttQos.AtLeastOnce;
        private readonly IMqttPacketManager _packetManager;
        private readonly IMqttTopicEvaluator _topicEvaluator;

        public override NodeType NodeType => NodeType.Server;

        private readonly PacketIdProvider _packetIdProvider = new();

        protected readonly MqttCounters MqttCounters;

        public MqttBroker(IMqttPacketManager packetManager,
            IMqttTopicEvaluator mqttTopicEvaluator,
            int index, string name, string address, string protocolType, 
            string specification, TicksOptions ticksOptions, bool enableCounters
           )
            : base(index, name, address, protocolType, specification, 
                  ticksOptions, enableCounters)
        {
            this.PacketReceived += MqttBroker_PacketReceived;
            this._packetManager = packetManager;
            this._topicEvaluator = mqttTopicEvaluator;

            MqttCounters = new MqttCounters(Name, Name.Substring(0, 2), "MqttBroker", "MB", ticksOptions, enableCounters);
            counters.AddCounter(MqttCounters);
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
                case MqttPacketType.Publish:
                    ProcessFromClientPublish(packet, _packetManager.BytesToPacket<PublishPacket>(packet.Payload));
                    break;
                case MqttPacketType.PublishReceived:
                    break;
                case MqttPacketType.PublishComplete:
                    break;
                case MqttPacketType.PingRequest:
                    break;
                case MqttPacketType.PublishAck:
                    ProcessToClientPublishAck(packet, _packetManager.BytesToPacket<PublishAckPacket>(packet.Payload));
                    break;
                case MqttPacketType.Connect:
                    var contextPacket = (NetworkPacket<(string Server, bool CleanSession)>)packet;
                    ProcessConnect(packet, _packetManager.BytesToPacket<ConnectPacket>(packet.Payload), contextPacket.TypedContext);
                    break;
                case MqttPacketType.Disconnect:
                    break;
                case MqttPacketType.Subscribe:
                    var subscribeContextPacket = (NetworkPacket<(string TopicFilter, MqttQos Qos)>)packet;
                    ProcessSubscribe(packet, _packetManager.BytesToPacket<SubscribePacket>(packet.Payload), subscribeContextPacket.TypedContext);
                    break;
                case MqttPacketType.Unsubscribe:
                    break;
                default:
                    return;
            }

            MqttCounters.PacketTypeCounters[payload.Type].Increment();
        }

        private void ProcessFromClientPublish(INetworkPacket packet, PublishPacket? publishPacket)
        {
            if (publishPacket == null)
            {
                return;
            }

            NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);

            var clientId = packet.From;

            ValidatePublish(clientId, publishPacket);

            var qos = _maximumQualityOfService;

            var session = _sessionRepository.Read(clientId);

            if (session == null)
            {
                throw new MqttException($"Client Session {clientId} Not Found");
            }

            if (qos == MqttQos.ExactlyOnce && session.GetPendingAcknowledgements()
                .Any(ack => ack.Type == MqttPacketType.PublishReceived && ack.PacketId == publishPacket?.PacketId))
            {
            }

            if (qos == MqttQos.AtLeastOnce)
            {
                SendPublishAck(packet, clientId, qos, publishPacket);
            }

            var subscriptions = _sessionRepository
                .ReadAll()
                .SelectMany(s => s.GetSubscriptions())
                .Where(x => _topicEvaluator.Matches(publishPacket.Topic, x.TopicFilter))
                .ToArray();

            foreach (var subscription in subscriptions)
            {
                ProcessToClientPublish(packet, subscription, publishPacket);
            }

            return;
        }

        private bool ProcessToClientPublish(
            INetworkPacket packet, ClientSubscription subscription, PublishPacket? publishPacket)
        {
            if (publishPacket == null)
            {
                return false;
            }

            var supportedQos = publishPacket.QualityOfService > subscription.MaximumQualityOfService
                ? subscription.MaximumQualityOfService
                : publishPacket.QualityOfService;

            ushort? packetId = supportedQos == MqttQos.AtMostOnce ? null : (ushort?)_packetIdProvider.GetPacketId();
            var retain = publishPacket.Retain;
            var subscriptionPublish = new PublishPacket(publishPacket.Topic, supportedQos, retain, duplicated: false, packetId: packetId)
            {
                Payload = publishPacket.Payload
            };
            var packetPayload = GetPacket(
                NetworkSimulator!.GetPacketId(), subscription.ClientId, NodeType.Client, packet.FromIndex,
                    _packetManager.PacketToBytes(subscriptionPublish), ProtocolType, "MQTT Publish");

            if (subscriptionPublish.QualityOfService > MqttQos.AtMostOnce)
            {
                SaveMessage(subscriptionPublish, subscription.ClientId, PendingMessageStatus.PendingToAcknowledge);
            }

            var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref packetPayload, $"Publish {Name} to  Subscriber {packetPayload.To} with topic {publishPacket.Topic}");
            MqttCounters.PacketTypeCounters[subscriptionPublish.Type].Increment();
            
            var sendResult = Send(packetPayload);
            return sendResult;
        }

        private void ProcessToClientPublishAck(INetworkPacket packet, PublishAckPacket? publishAckPacket)
        {
            if (publishAckPacket == null)
            {
                return;
            }

            var clientId = packet.From;

            var session = _sessionRepository.Read(clientId);

            if (session == null)
            {
                throw new MqttException($"Client Session {clientId} Not Found");
            }

            var pendingMessage = session
                .GetPendingMessages()
                .FirstOrDefault(p => p.PacketId.HasValue && p.PacketId.Value == publishAckPacket.PacketId);

            session.RemovePendingMessage(pendingMessage);

            _sessionRepository.Update(session);

            NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);
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

        private void SendPublishAck(INetworkPacket packet, string clientId, MqttQos qos, PublishPacket publishPacket)
        {
            var ack = new PublishAckPacket(publishPacket.PacketId ?? 0);
            var ackPayload = _packetManager.PacketToBytes(ack) ?? Array.Empty<byte>();
            var reversePacket = NetworkSimulator!.GetReversePacket(packet, ackPayload.ToArray(), "MQTT PubAck");
            var scope = NetworkSimulator.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref reversePacket,
                $"Publish Ack {packet.From} to {packet.To} with topic {publishPacket.Topic}");
            NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, packet, NetworkLoggerType.Send, 
                $"Send MQTT publish ack {packet.From} to {packet.To} with {publishPacket.Topic} (QoS={publishPacket.QualityOfService})", ProtocolType, "MQTT PubAck");
            Send(reversePacket);
            NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref reversePacket);
            MqttCounters.PacketTypeCounters[ack.Type].Increment();
        }

        private void ValidatePublish(string clientId, PublishPacket publishPacket)
        {
            if (publishPacket.QualityOfService != MqttQos.AtMostOnce && !publishPacket.PacketId.HasValue)
            {
                throw new MqttException("NetworkMonitoringPacket Id Required");
            }

            if (publishPacket.QualityOfService == MqttQos.AtMostOnce && publishPacket.PacketId.HasValue)
            {
                throw new MqttException("NetworkMonitoringPacket Id Not Allowed");
            }
        }

        private bool ProcessSubscribe(INetworkPacket packet, SubscribePacket? subscribePacket, (string TopicFilter, MqttQos Qos)? context)
        {
            if (subscribePacket == null)
            {
                return false;
            }

            var clientId = packet.From;

            var session = _sessionRepository.Read(clientId);

            if (session == null)
            {
                throw new MqttException($"Client Session {clientId} Not Found");
            }

            var returnCodes = new List<SubscribeReturnCode>();

            foreach (var subscription in subscribePacket.Subscriptions)
            {
                try
                {
                    if (!_topicEvaluator.IsValidTopicFilter(subscription.TopicFilter))
                    {
                        returnCodes.Add(SubscribeReturnCode.Failure);
                        continue;
                    }

                    var clientSubscription = session
                        .GetSubscriptions()
                        .FirstOrDefault(s => s.TopicFilter == subscription.TopicFilter);

                    if (clientSubscription != null)
                    {
                        clientSubscription.MaximumQualityOfService = subscription.MaximumQualityOfService;
                    }
                    else
                    {
                        clientSubscription = new ClientSubscription
                        {
                            ClientId = clientId,
                            TopicFilter = subscription.TopicFilter,
                            MaximumQualityOfService = subscription.MaximumQualityOfService
                        };

                        session.AddSubscription(clientSubscription);
                    }

                    var supportedQos = _maximumQualityOfService;
                    var returnCode = supportedQos.ToReturnCode();

                    returnCodes.Add(returnCode);
                }
                catch (Exception)
                {
                    returnCodes.Add(SubscribeReturnCode.Failure);
                }
            }

            _sessionRepository.Update(session);

            var subscribeAck = new SubscribeAckPacket(subscribePacket.PacketId, returnCodes.ToArray());

            var packetPayload = context != null ? GetContextPacket(NetworkSimulator!.GetPacketId(), clientId, NodeType.Client, packet.FromIndex,
                _packetManager.PacketToBytes(subscribeAck), ProtocolType, context.Value, "MQTT SubAck", packet.Id) :
                GetPacket(NetworkSimulator!.GetPacketId(), clientId, NodeType.Client, packet.FromIndex,
                    _packetManager.PacketToBytes(subscribeAck), ProtocolType, "MQTT SubAck", packet.Id);


            NetworkSimulator!.Monitoring.Push(NetworkSimulator.TotalTicks, packet, NetworkLoggerType.Send,
                $"Send MQTT subscribe ack {packet.From} to {packet.To}", ProtocolType, "MQTT SubAck");
            Send(packetPayload);
            MqttCounters.PacketTypeCounters[subscribeAck.Type].Increment();
            return true;
        }

        private bool ProcessConnect(INetworkPacket packet, ConnectPacket? connectPacket, (string Server, bool CleanSession)? context)
        {
            if (connectPacket == null) return false;

            var clientId = connectPacket.ClientId ?? string.Empty;

            var session = _sessionRepository.Read(packet.From);

            if (connectPacket.CleanSession && session != null)
            {
                _sessionRepository.Delete(packet.From);
                session = null;
            }

            if ( session == null)
            {
                session = new ClientSession(packet.From, clientId, connectPacket.CleanSession);

                _sessionRepository.Update(session);
            }

            var sessionPresent = connectPacket.CleanSession ? false : session != null;
            var clientName = packet.From;
            var connecktAck = new ConnectAckPacket(MqttConnectionStatus.Accepted, sessionPresent);
            var packetPayload = context != null ? GetContextPacket(NetworkSimulator!.GetPacketId(), clientName, NodeType.Client, packet.FromIndex,
                _packetManager.PacketToBytes(connecktAck), ProtocolType, context.Value, "MQTT ConnAck", packet.Id) : 
                GetPacket(NetworkSimulator!.GetPacketId(), clientName, NodeType.Client, packet.FromIndex,
                    _packetManager.PacketToBytes(connecktAck), ProtocolType, "MQTT ConnAck", packet.Id);

            var sendResult = Send(packetPayload);
            MqttCounters.PacketTypeCounters[connecktAck.Type].Increment();
            return sendResult;
        }

        private void MqttBroker_PacketReceived(object? sender, INetworkPacket e)
        {

        }

        public override void Clear()
        {
            PacketReceived -= MqttBroker_PacketReceived;
            _sessionRepository.Clear();
            base.Clear();
        }
    }
}
