using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Mqtt.Internals;
using Microsoft.VisualBasic;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Formats.Asn1.AsnWriter;

namespace EntityFX.MqttY.Mqtt
{
    internal class MqttBroker : Server, IMqttBroker
    {
        private readonly IRepository<ClientSession> _sessionRepository = new InMemoryRepository<ClientSession>();

        private readonly MqttQos MaximumQualityOfService = MqttQos.AtLeastOnce;

        readonly IMqttTopicEvaluator topicEvaluator;

        public override NodeType NodeType => NodeType.Server;

        private readonly PacketIdProvider _packetIdProvider = new();

        public MqttBroker(int index, string name, string address, string protocolType, INetwork network, INetworkGraph networkGraph)
            : base(index, name, address, protocolType, network, networkGraph)
        {
            this.PacketReceived += MqttBroker_PacketReceived;
            topicEvaluator = new MqttTopicEvaluator(true);
        }

        protected override Packet OnReceivedWithResponse(Packet packet)
        {
            if (packet.Payload.Length >= 1 &&  packet.Payload[0] == 0xFF)
            {
                return base.OnReceivedWithResponse(packet);
            }
            var payload = packet.Payload.BytesToPacket<PacketBase>();
            if (payload == null)
            {
                return base.OnReceivedWithResponse(packet);
            }

            IPacket? result = null;
            var category = string.Empty;
            switch (payload!.Type)
            {
                case MqttPacketType.Connect:
                    result = ProcessConnect(packet, packet.Payload.BytesToPacket<ConnectPacket>());
                    category = "MQTT ConnAck";
                    break;
                case MqttPacketType.Disconnect:
                    break;
                case MqttPacketType.Subscribe:
                    result = ProcessSubscribe(packet.From, packet.Payload.BytesToPacket<SubscribePacket>());
                    category = "MQTT SubAck";
                    break;
                case MqttPacketType.Unsubscribe:
                    break;
                default:
                    break;
            }

            var resultPayload = result?.PacketToBytes() ?? Array.Empty<byte>();
            return NetworkGraph.GetReversePacket(packet, resultPayload, category);
        }

        protected override async Task OnReceived(Packet packet)
        {
            var payload = packet.Payload.BytesToPacket<PacketBase>();
            if (payload == null)
            {
                await base.OnReceived(packet);
            }
            NetworkGraph.Monitoring.WithEndScope(ref packet);
            switch (payload!.Type)
            {
                case MqttPacketType.Publish:
                    await ProcessFromClientPublish(packet, packet.From, packet.Payload.BytesToPacket<PublishPacket>());
                    break;
                case MqttPacketType.PublishReceived:
                    break;
                case MqttPacketType.PublishComplete:
                    break;
                case MqttPacketType.PingRequest:
                    break;
                case MqttPacketType.PublishAck:
                    await ProcessToClientPublishAck(packet, packet.From, packet.Payload.BytesToPacket<PublishAckPacket>());
                    break;
                default:
                    break;
            }
        }

        private async Task ProcessFromClientPublish(Packet packet, string clientId, PublishPacket? publishPacket)
        {
            if (publishPacket == null)
            {
                return;
            }

            NetworkGraph.Monitoring.WithEndScope(ref packet);

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
                await ProcessToClientPublishAsync(subscription, publishPacket);
            }

            return;
        }

        private async Task<bool> ProcessToClientPublishAsync(ClientSubscription subscription, PublishPacket? publishPacket)
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

            var packetPayload = GetPacket(subscription.ClientId, NodeType.Client, subscriptionPublish.PacketToBytes(), "MQTT Publish");

            if (subscriptionPublish.QualityOfService > MqttQos.AtMostOnce)
            {
                SaveMessage(subscriptionPublish, subscription.ClientId, PendingMessageStatus.PendingToAcknowledge);
            }

            var scope = NetworkGraph.Monitoring.WithBeginScope(ref packetPayload, $"Publish {Name} to  Subscriber {packetPayload.To} with topic {publishPacket.Topic}");
            await SendAsync(packetPayload);
            return true;
        }

        private Task ProcessToClientPublishAck(Packet payload, string clientId, PublishAckPacket? publishAckPacket)
        {
            if (publishAckPacket == null)
            {
                return Task.CompletedTask;
            }

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

            NetworkGraph.Monitoring.WithEndScope(ref payload);

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

        private async Task SendPublishAck(Packet packet, string clientId, MqttQos qos, PublishPacket publishPacket)
        {
            var ack = new PublishAckPacket(publishPacket.PacketId ?? 0);
            var ackPayload = ack.PacketToBytes() ?? Array.Empty<byte>();
            var reversePacket = NetworkGraph.GetReversePacket(packet, ackPayload.ToArray(), "MQTT PubAck");
            var scope = NetworkGraph.Monitoring.WithBeginScope(ref reversePacket,
                $"Publish Ack {packet.From} to {packet.To} with topic {publishPacket.Topic}");
            await SendAsync(reversePacket);
            NetworkGraph.Monitoring.WithEndScope(ref reversePacket);
        }

        private void ValidatePublish(string clientId, PublishPacket publishPacket)
        {
            if (publishPacket.QualityOfService != MqttQos.AtMostOnce && !publishPacket.PacketId.HasValue)
            {
                throw new MqttException("Packet Id Required");
            }

            if (publishPacket.QualityOfService == MqttQos.AtMostOnce && publishPacket.PacketId.HasValue)
            {
                throw new MqttException("Packet Id Not Allowed");
            }
        }

        private SubscribeAckPacket? ProcessSubscribe(string clientId, SubscribePacket? subscribePacket)
        {
            if (subscribePacket == null)
            {
                return null;
            }

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

            return new SubscribeAckPacket(subscribePacket.PacketId, returnCodes.ToArray());
        }

        private ConnectAckPacket? ProcessConnect(Packet packet, ConnectPacket? connectPacket)
        {
            if (connectPacket == null) return null;

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

            return new ConnectAckPacket(MqttConnectionStatus.Accepted, sessionPresent);
        }

        private void MqttBroker_PacketReceived(object? sender, Packet e)
        {

        }
    }
}
