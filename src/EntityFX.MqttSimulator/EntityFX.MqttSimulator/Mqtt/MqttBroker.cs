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

namespace EntityFX.MqttY.Mqtt
{
    internal class MqttBroker : Server, IMqttBroker
    {
        private readonly ConcurrentDictionary<string, ClientSession> sessions = new ConcurrentDictionary<string, ClientSession>();

        private readonly MqttQos MaximumQualityOfService = MqttQos.AtLeastOnce;

        readonly IMqttTopicEvaluator topicEvaluator;

        public override NodeType NodeType => NodeType.Server;

        public MqttBroker(int index, string name, string address, string protocolType, INetwork network, INetworkGraph networkGraph)
            : base(index, name, address, protocolType, network, networkGraph)
        {
            this.PacketReceived += MqttBroker_PacketReceived;
            topicEvaluator = new MqttTopicEvaluator(true);
        }

        protected override Packet ProcessReceive(Packet packet)
        {
            var payload = packet.Payload.BytesToPacket<PacketBase>();
            if (payload == null)
            {
                base.ProcessReceive(packet);
            }

            IPacket? result = null;
            switch (payload!.Type)
            {
                case MqttPacketType.Connect:
                    result = ProcessConnect(packet.Payload.BytesToPacket<ConnectPacket>());
                    break;
                case MqttPacketType.Publish:
                    break;
                case MqttPacketType.PublishAck:
                    break;
                case MqttPacketType.PublishReceived:
                    break;
                case MqttPacketType.PublishRelease:
                    break;
                case MqttPacketType.PublishComplete:
                    break;
                case MqttPacketType.Subscribe:
                    result = ProcessSubscribe(packet.From, packet.Payload.BytesToPacket<SubscribePacket>());
                    break;
                case MqttPacketType.SubscribeAck:
                    break;
                case MqttPacketType.Unsubscribe:
                    break;
                case MqttPacketType.UnsubscribeAck:
                    break;
                case MqttPacketType.PingRequest:
                    break;
                case MqttPacketType.PingResponse:
                    break;
                case MqttPacketType.Disconnect:
                    break;
                default:
                    break;
            }

            var resultPayload = result?.PacketToBytes() ?? Array.Empty<byte>();
            return NetworkGraph.GetReversePacket(packet, resultPayload);
        }

        private SubscribeAckPacket? ProcessSubscribe(string clientId, SubscribePacket? subscribePacket)
        {
            if (subscribePacket == null)
            {
                return null;
            }

            sessions.TryGetValue(clientId, out ClientSession? session);

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

                    //TODO: add retained send
                    //await SendRetainedMessagesAsync(clientSubscription, channel)
                    //    .ConfigureAwait(continueOnCapturedContext: false);

                    var supportedQos = MaximumQualityOfService;
                    var returnCode = supportedQos.ToReturnCode();

                    returnCodes.Add(returnCode);
                }
                catch (Exception)
                {
                    returnCodes.Add(SubscribeReturnCode.Failure);
                }
            }

            sessions.AddOrUpdate(session.Id, session, (key, value) => session);

            return new SubscribeAckPacket(subscribePacket.PacketId, returnCodes.ToArray());
        }

        private ConnectAckPacket? ProcessConnect(ConnectPacket? connectPacket)
        {
            if (connectPacket == null) return null;

            var clientId = connectPacket.ClientId ?? string.Empty;

            sessions.TryGetValue(clientId, out ClientSession? session);

            if (connectPacket.CleanSession && session != null)
            {
                sessions.TryRemove(clientId, out var _);
                session = null;
            }

            if ( session == null)
            {
                session = new ClientSession(clientId, connectPacket.CleanSession);

                sessions.AddOrUpdate(session.Id, session, (key, value) => session);
            }

            var sessionPresent = connectPacket.CleanSession ? false : session != null;

            return new ConnectAckPacket(MqttConnectionStatus.Accepted, sessionPresent);
        }

        private void MqttBroker_PacketReceived(object? sender, Packet e)
        {

        }
    }
}
