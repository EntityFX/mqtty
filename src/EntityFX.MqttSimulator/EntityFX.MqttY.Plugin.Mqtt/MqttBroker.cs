using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Counter;
using EntityFX.MqttY.Plugin.Mqtt.Internals;

namespace EntityFX.MqttY.Plugin.Mqtt
{
    internal class MqttBroker : Server, IMqttBroker, IMeasurementSource, IMqttBrokerMetricsSink
    {
        private readonly IRepository<ClientSession> _sessionRepository = new InMemoryRepository<ClientSession>();

        private readonly MqttQos _maximumQualityOfService = MqttQos.ExactlyOnce;
        private readonly IMqttPacketManager _packetManager;
        private readonly IMqttTopicEvaluator _topicEvaluator;

        private readonly MqttBrokerProfile? _profile;
        private readonly BrokerRateLimiter[] _rateLimiters = new BrokerRateLimiter[3];
        private readonly BrokerProcessingQueue _processingQueue = new();
        private readonly BrokerFailModel _failModel;
        private readonly TimeSpan _tickPeriod;
        private readonly int[] _processingTicks = new int[3];
        private readonly double[] _failProb = new double[3];
        private readonly BrokerMeasurement _measurement = new();

        public override NodeType NodeType => NodeType.Server;

        public override bool IsQuiescent => base.IsQuiescent && _processingQueue.IsEmpty &&
            _sessionRepository.ReadAll().All(session =>
                !session.GetPendingMessages().Any() &&
                !session.GetPendingAcknowledgements().Any(ack => ack.Type != MqttPacketType.PublishComplete));

        private readonly PacketIdProvider _packetIdProvider = new();

        protected readonly MqttCounters MqttCounters;

        public BrokerMetricsSnapshot GetMetrics()
        {
            var snapshot = _measurement.Snapshot(NetworkSimulator?.TotalTicks ?? 0, _tickPeriod);
            MqttCounters.ApplyMeasurement(snapshot);
            return snapshot;
        }

        public void ResetMeasurement(long startTick) => _measurement.Reset(startTick);

        void IMqttBrokerMetricsSink.CompletePublisher(
            string publisher, MqttQos qos, ushort packetId, long tick)
        {
            var elapsedTicks = _measurement.CompletePublisher(publisher, qos, packetId, tick);
            if (elapsedTicks.HasValue)
                MqttCounters.ObserveLatency(elapsedTicks.Value * _tickPeriod.TotalMilliseconds);
        }

        void IMqttBrokerMetricsSink.CompleteDelivery(long outgoingPublishPacketId) =>
            _measurement.DeliveryCompleted(outgoingPublishPacketId);

        public MqttBroker(IMqttPacketManager packetManager,
            IMqttTopicEvaluator mqttTopicEvaluator,
            int index, string name, string address, string protocolType, 
            string specification, TicksOptions ticksOptions, bool enableCounters,
            MqttBrokerProfile? brokerProfile = null
           )
            : base(index, name, address, protocolType, specification, 
                  ticksOptions, enableCounters)
        {
            this.PacketReceived += MqttBroker_PacketReceived;
            this.ClientConnected += (_, _) => UpdateProfileFromClients();
            this.ClientDisconnected += (_, clientName) =>
            {
                var session = _sessionRepository.Read(clientName);
                if (session?.Clean == true)
                {
                    _sessionRepository.Delete(clientName);
                }

                UpdateProfileFromClients();
            };
            this._packetManager = packetManager;
            this._topicEvaluator = mqttTopicEvaluator;

            MqttCounters = new MqttCounters(Name, Name.Substring(0, 2), "MqttBroker", "MB", ticksOptions, enableCounters);
            counters.AddCounter(MqttCounters);

            _profile = brokerProfile;
            _tickPeriod = ticksOptions.TickPeriod;
            _failModel = new BrokerFailModel(seed: BrokerSeed(name));

            if (brokerProfile != null)
            {
                UpdateProfileFromClients();
            }
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
                    ProcessFromClientPublishReceived(packet,
                        _packetManager.BytesToPacket<PublishReceivedPacket>(packet.Payload));
                    break;
                case MqttPacketType.PublishRelease:
                    ProcessFromClientPublishRelease(packet, _packetManager.BytesToPacket<PublishReleasePacket>(packet.Payload));
                    break;
                case MqttPacketType.PublishComplete:
                    ProcessFromClientPublishComplete(packet,
                        _packetManager.BytesToPacket<PublishCompletePacket>(packet.Payload));
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

            var tracker = _measurement.BeginAttempt(packet.From, publishPacket.QualityOfService,
                publishPacket.PacketId, packet.Id, NetworkSimulator!.TotalTicks);
            if (tracker.Admitted)
            {
                ProcessFromClientPublishCore(packet, publishPacket);
                return;
            }

            // Обратная совместимость: без профиля брокер обрабатывает сообщения
            // без ограничений по RPS, задержке и отказам.
            if (_profile == null)
            {
                _measurement.Admit(tracker);
                ProcessFromClientPublishCore(packet, publishPacket);
                return;
            }

            var qosIndex = (int)publishPacket.QualityOfService;

            // 1. Лимитер RPS (пропускная способность).
            if (!_rateLimiters[qosIndex].TryAcquire(NetworkSimulator!.TotalTicks))
            {
                MqttCounters.RefuseByRateLimit(publishPacket.QualityOfService);
                _measurement.RejectByRate(tracker);
                return;
            }

            // 2. Вероятностный отказ.
            if (_failModel.ShouldFail(_failProb[qosIndex]))
            {
                MqttCounters.RefuseByFailRate(publishPacket.QualityOfService);
                _measurement.FailPublish(tracker);
                return;
            }

            // 3. Постановка в очередь обработки с задержкой.
            _measurement.Admit(tracker);
            _processingQueue.Enqueue(packet, _processingTicks[qosIndex]);
        }

        private void ProcessFromClientPublishCore(INetworkPacket packet, PublishPacket? publishPacket)
        {
            if (publishPacket == null)
            {
                return;
            }

            NetworkSimulator!.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref packet);

            var clientId = packet.From;

            ValidatePublish(clientId, publishPacket);

            var qos = publishPacket.QualityOfService > _maximumQualityOfService
                ? _maximumQualityOfService
                : publishPacket.QualityOfService;

            var session = _sessionRepository.Read(clientId);

            if (session == null)
            {
                throw new MqttException($"Client Session {clientId} Not Found");
            }

            if (qos == MqttQos.ExactlyOnce)
            {
                var packetId = publishPacket.PacketId!.Value;
                if (session.GetPendingMessages().Any(message =>
                        message.Status == PendingMessageStatus.AwaitingPublishRelease &&
                        message.PacketId == packetId))
                {
                    SendPublishReceived(packet, publishPacket);
                    return;
                }

                var completed = session.GetPendingAcknowledgements().FirstOrDefault(ack =>
                    ack.Type == MqttPacketType.PublishComplete && ack.PacketId == packetId);
                session.RemovePendingAcknowledgement(completed);

                SaveMessage(publishPacket, clientId, PendingMessageStatus.AwaitingPublishRelease);
                SendPublishReceived(packet, publishPacket);
                return;
            }

            if (qos == MqttQos.AtLeastOnce)
            {
                SendPublishAck(packet, clientId, qos, publishPacket);
            }

            FanOutAndCount(packet, publishPacket);
        }

        private void FanOutAndCount(INetworkPacket packet, PublishPacket publishPacket)
        {
            var tracker = _measurement.Find(packet.From, publishPacket.QualityOfService,
                publishPacket.PacketId, packet.Id);
            var subscriptions = _sessionRepository
                .ReadAll()
                .SelectMany(s => s.GetSubscriptions())
                .Where(x => _topicEvaluator.Matches(publishPacket.Topic, x.TopicFilter))
                .ToArray();

            foreach (var subscription in subscriptions)
            {
                ProcessToClientPublish(packet, subscription, publishPacket, tracker);
            }

            MqttCounters.IncrementPublish(publishPacket.QualityOfService);
            _measurement.CompleteFanOut(tracker);
        }

        private bool ProcessToClientPublish(
            INetworkPacket packet, ClientSubscription subscription, PublishPacket? publishPacket,
            BrokerMeasurement.PublishTracker? tracker)
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
            _measurement.ExpectDelivery(tracker, packetPayload.Id);

            if (subscriptionPublish.QualityOfService > MqttQos.AtMostOnce)
            {
                var status = subscriptionPublish.QualityOfService == MqttQos.ExactlyOnce
                    ? PendingMessageStatus.AwaitingPublishReceived
                    : PendingMessageStatus.PendingToAcknowledge;
                SaveMessage(subscriptionPublish, subscription.ClientId, status);
            }

            var scope = NetworkSimulator!.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref packetPayload, $"Publish {Name} to  Subscriber {packetPayload.To} with topic {publishPacket.Topic}");
            MqttCounters.PacketTypeCounters[subscriptionPublish.Type].Increment();
            
            var sendResult = Send(packetPayload);
            if (!sendResult)
                _measurement.DeliveryDropped(packetPayload.Id);
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

        private void SendPublishReceived(INetworkPacket packet, PublishPacket publishPacket)
        {
            var received = new PublishReceivedPacket(publishPacket.PacketId ?? 0);
            var payload = _packetManager.PacketToBytes(received) ?? Array.Empty<byte>();
            var reversePacket = NetworkSimulator!.GetReversePacket(packet, payload.ToArray(), "MQTT PubRec");

            Send(reversePacket);
            MqttCounters.PacketTypeCounters[received.Type].Increment();
        }

        private void ProcessFromClientPublishReceived(
            INetworkPacket packet, PublishReceivedPacket? publishReceivedPacket)
        {
            if (publishReceivedPacket == null)
            {
                return;
            }

            var session = _sessionRepository.Read(packet.From);
            if (session == null)
            {
                throw new MqttException($"Client Session {packet.From} Not Found");
            }

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
            SendPublishRelease(packet, publishReceivedPacket.PacketId);
        }

        private void ProcessFromClientPublishRelease(INetworkPacket packet, PublishReleasePacket? publishReleasePacket)
        {
            if (publishReleasePacket == null)
            {
                return;
            }

            var clientId = packet.From;
            var session = _sessionRepository.Read(clientId);

            if (session == null)
            {
                throw new MqttException($"Client Session {clientId} Not Found");
            }

            var pendingMessage = session.GetPendingMessages().FirstOrDefault(message =>
                message.PacketId == publishReleasePacket.PacketId &&
                message.Status == PendingMessageStatus.AwaitingPublishRelease);

            if (pendingMessage != null)
            {
                var publishPacket = new PublishPacket(
                    pendingMessage.Topic,
                    pendingMessage.QualityOfService,
                    pendingMessage.Retain,
                    pendingMessage.Duplicated,
                    pendingMessage.PacketId)
                {
                    Payload = pendingMessage.Payload
                };

                session.RemovePendingMessage(pendingMessage);
                session.AddPendingAcknowledgement(new PendingAcknowledgement
                {
                    Type = MqttPacketType.PublishComplete,
                    PacketId = publishReleasePacket.PacketId
                });
                _sessionRepository.Update(session);
                FanOutAndCount(packet, publishPacket);
            }

            SendPublishComplete(packet, publishReleasePacket.PacketId);
        }

        private void ProcessFromClientPublishComplete(
            INetworkPacket packet, PublishCompletePacket? publishCompletePacket)
        {
            if (publishCompletePacket == null)
            {
                return;
            }

            var session = _sessionRepository.Read(packet.From);
            if (session == null)
            {
                throw new MqttException($"Client Session {packet.From} Not Found");
            }

            var pendingMessage = session.GetPendingMessages().FirstOrDefault(message =>
                message.PacketId == publishCompletePacket.PacketId &&
                message.Status == PendingMessageStatus.AwaitingPublishComplete);
            session.RemovePendingMessage(pendingMessage);
            _sessionRepository.Update(session);
        }

        private void SendPublishRelease(INetworkPacket packet, ushort packetId)
        {
            var release = new PublishReleasePacket(packetId);
            var payload = _packetManager.PacketToBytes(release) ?? Array.Empty<byte>();
            var reversePacket = NetworkSimulator!.GetReversePacket(packet, payload, "MQTT PubRel");
            Send(reversePacket);
            MqttCounters.PacketTypeCounters[release.Type].Increment();
        }

        private void SendPublishComplete(INetworkPacket packet, ushort packetId)
        {
            var complete = new PublishCompletePacket(packetId);
            var payload = _packetManager.PacketToBytes(complete) ?? Array.Empty<byte>();
            var reversePacket = NetworkSimulator!.GetReversePacket(packet, payload, "MQTT PubComp");
            Send(reversePacket);
            MqttCounters.PacketTypeCounters[complete.Type].Increment();
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

                    var supportedQos = subscription.MaximumQualityOfService > _maximumQualityOfService
                        ? _maximumQualityOfService
                        : subscription.MaximumQualityOfService;
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
            var sessionPresent = !connectPacket.CleanSession && session != null;

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

        public override void Refresh()
        {
            base.Refresh();

            foreach (var packet in _processingQueue.DrainReady())
            {
                var publishPacket = _packetManager.BytesToPacket<PublishPacket>(packet.Payload);
                ProcessFromClientPublishCore(packet, publishPacket);
            }
        }

        private void UpdateProfileFromClients()
        {
            if (_profile == null)
            {
                return;
            }

            var clients = GetServerClients().Count();

            var sample0 = _profile.Qos0.Interpolate(clients);
            var sample1 = _profile.Qos1.Interpolate(clients);
            var sample2 = _profile.Qos2.Interpolate(clients);

            _rateLimiters[0] = new BrokerRateLimiter(sample0.Rps, _tickPeriod);
            _rateLimiters[1] = new BrokerRateLimiter(sample1.Rps, _tickPeriod);
            _rateLimiters[2] = new BrokerRateLimiter(sample2.Rps, _tickPeriod);

            _processingTicks[0] = TicksFromMs(sample0.LatencyMs);
            _processingTicks[1] = TicksFromMs(sample1.LatencyMs);
            _processingTicks[2] = TicksFromMs(sample2.LatencyMs);

            _failProb[0] = sample0.FailRate;
            _failProb[1] = sample1.FailRate;
            _failProb[2] = sample2.FailRate;
        }

        private int TicksFromMs(double latencyMs)
        {
            if (_tickPeriod <= TimeSpan.Zero)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Ceiling(latencyMs / _tickPeriod.TotalMilliseconds));
        }

        private static int BrokerSeed(string name)
        {
            unchecked
            {
                var hash = 17;
                foreach (var c in name)
                {
                    hash = hash * 31 + c;
                }
                return hash;
            }
        }

        public override void Clear()
        {
            PacketReceived -= MqttBroker_PacketReceived;
            _sessionRepository.Clear();
            _processingQueue.Clear();
            base.Clear();
        }
    }
}
