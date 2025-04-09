using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Counter;
using EntityFX.MqttY.Mqtt.Internals;

namespace EntityFX.MqttY.Mqtt
{
    internal class MqttBroker : Server, IMqttBroker
    {
        private readonly IRepository<ClientSession> _sessionRepository = new InMemoryRepository<ClientSession>();

        private readonly MqttQos MaximumQualityOfService = MqttQos.AtLeastOnce;
        private readonly IMqttPacketManager packetManager;
        private readonly IMqttTopicEvaluator topicEvaluator;

        public override NodeType NodeType => NodeType.Server;

        private readonly PacketIdProvider _packetIdProvider = new();

        protected readonly MqttCounters mqttCounters = new MqttCounters("Mqtt");

        public MqttBroker(IMqttPacketManager packetManager, INetwork network, INetworkGraph networkGraph, IMqttTopicEvaluator mqttTopicEvaluator,
            int index, string name, string address, string protocolType, 
            string specification
           )
            : base(index, name, address, protocolType, specification, network, networkGraph)
        {
            this.PacketReceived += MqttBroker_PacketReceived;
            this.packetManager = packetManager;
            this.topicEvaluator = mqttTopicEvaluator;
            counters.AddCounter(mqttCounters);
        }

        protected override async Task OnReceived(NetworkPacket packet)
        {
            var payload = await packetManager.BytesToPacket<PacketBase>(packet.Payload);
            if (payload == null)
            {
                await base.OnReceived(packet);
            }
            NetworkGraph.Monitoring.WithEndScope(ref packet);
            switch (payload!.Type)
            {
                case MqttPacketType.Publish:
                    await ProcessFromClientPublish(packet, await packetManager.BytesToPacket<PublishPacket>(packet.Payload));
                    break;
                case MqttPacketType.PublishReceived:
                    break;
                case MqttPacketType.PublishComplete:
                    break;
                case MqttPacketType.PingRequest:
                    break;
                case MqttPacketType.PublishAck:
                    await ProcessToClientPublishAck(packet, await packetManager.BytesToPacket<PublishAckPacket>(packet.Payload));
                    break;
                case MqttPacketType.Connect:
                    await ProcessConnect(packet, await packetManager.BytesToPacket<ConnectPacket>(packet.Payload));
                    break;
                case MqttPacketType.Disconnect:
                    break;
                case MqttPacketType.Subscribe:
                    await ProcessSubscribe(packet, await packetManager.BytesToPacket<SubscribePacket>(packet.Payload));
                    break;
                case MqttPacketType.Unsubscribe:
                    break;
                default:
                    return;
            }

            mqttCounters.PacketTypeCounters[payload.Type].Increment();
        }

        private async Task ProcessFromClientPublish(NetworkPacket packet, PublishPacket? publishPacket)
        {
            if (publishPacket == null)
            {
                return;
            }

            NetworkGraph.Monitoring.WithEndScope(ref packet);

            var clientId = packet.From;

            ValidatePublish(clientId, publishPacket);

            var qos = MaximumQualityOfService;

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
                await SendPublishAck(packet, clientId, qos, publishPacket);
            }

            var subscriptions = _sessionRepository
                .ReadAll()
                .SelectMany(s => s.GetSubscriptions())
                .Where(x => topicEvaluator.Matches(publishPacket.Topic, x.TopicFilter))
                .ToArray();

            foreach (var subscription in subscriptions)
            {
                await ProcessToClientPublishAsync(packet, subscription, publishPacket);
            }

            return;
        }

        private async Task<bool> ProcessToClientPublishAsync(
            NetworkPacket packet, ClientSubscription subscription, PublishPacket? publishPacket)
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
                Guid.NewGuid(), subscription.ClientId, NodeType.Client, await packetManager.PacketToBytes(subscriptionPublish), ProtocolType, "MQTT Publish");

            if (subscriptionPublish.QualityOfService > MqttQos.AtMostOnce)
            {
                SaveMessage(subscriptionPublish, subscription.ClientId, PendingMessageStatus.PendingToAcknowledge);
            }

            var scope = NetworkGraph.Monitoring.WithBeginScope(ref packetPayload, $"Publish {Name} to  Subscriber {packetPayload.To} with topic {publishPacket.Topic}");
            mqttCounters.PacketTypeCounters[subscriptionPublish.Type].Increment();
            await SendAsync(packetPayload);
            return true;
        }

        private Task ProcessToClientPublishAck(NetworkPacket packet, PublishAckPacket? publishAckPacket)
        {
            if (publishAckPacket == null)
            {
                return Task.CompletedTask;
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

            NetworkGraph.Monitoring.WithEndScope(ref packet);

            return Task.CompletedTask;
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

        private async Task SendPublishAck(NetworkPacket packet, string clientId, MqttQos qos, PublishPacket publishPacket)
        {
            var ack = new PublishAckPacket(publishPacket.PacketId ?? 0);
            var ackPayload = await packetManager.PacketToBytes(ack) ?? Array.Empty<byte>();
            var reversePacket = NetworkGraph.GetReversePacket(packet, ackPayload.ToArray(), "MQTT PubAck");
            var scope = NetworkGraph.Monitoring.WithBeginScope(ref reversePacket,
                $"Publish Ack {packet.From} to {packet.To} with topic {publishPacket.Topic}");
            NetworkGraph.Monitoring.Push(packet, NetworkLoggerType.Send, 
                $"Send MQTT publish ack {packet.From} to {packet.To} with {publishPacket.Topic} (QoS={publishPacket.QualityOfService})", ProtocolType, "MQTT PubAck");
            await SendAsync(reversePacket);
            NetworkGraph.Monitoring.WithEndScope(ref reversePacket);
            mqttCounters.PacketTypeCounters[ack.Type].Increment();
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

        private async Task<bool> ProcessSubscribe(NetworkPacket packet, SubscribePacket? subscribePacket)
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
                    if (!topicEvaluator.IsValidTopicFilter(subscription.TopicFilter))
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

                    var supportedQos = MaximumQualityOfService;
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
            var packetPayload = GetPacket(Guid.NewGuid(), clientId, NodeType.Client, await packetManager.PacketToBytes(subscribeAck),
                ProtocolType, "MQTT SubAck", packet.Id);
            NetworkGraph.Monitoring.Push(packet, NetworkLoggerType.Send,
                $"Send MQTT subscribe ack {packet.From} to {packet.To}", ProtocolType, "MQTT SubAck");
            await SendAsync(packetPayload);
            mqttCounters.PacketTypeCounters[subscribeAck.Type].Increment();
            return true;
        }

        private async Task<bool> ProcessConnect(NetworkPacket packet, ConnectPacket? connectPacket)
        {
            if (connectPacket == null) return false;

            var clientId = connectPacket.ClientId ?? string.Empty;

            var session = _sessionRepository.Read(clientId);

            if (connectPacket.CleanSession && session != null)
            {
                _sessionRepository.Delete(clientId);
                session = null;
            }

            if ( session == null)
            {
                session = new ClientSession(clientId, connectPacket.CleanSession);

                _sessionRepository.Update(session);
            }

            var sessionPresent = connectPacket.CleanSession ? false : session != null;

            var connecktAck = new ConnectAckPacket(MqttConnectionStatus.Accepted, sessionPresent);
            var packetPayload = GetPacket(Guid.NewGuid(), clientId, NodeType.Client, 
                await packetManager.PacketToBytes(connecktAck), ProtocolType, "MQTT ConnAck", packet.Id);

            NetworkGraph.Monitoring.Push(packet, NetworkLoggerType.Send,
                $"Send MQTT connect ack {packet.From} to {packet.To} with Status={connecktAck.Status}", ProtocolType, "MQTT ConnAck");

            await SendAsync(packetPayload);
            mqttCounters.PacketTypeCounters[connecktAck.Type].Increment();
            return true;
        }

        private void MqttBroker_PacketReceived(object? sender, NetworkPacket e)
        {

        }
    }
}
