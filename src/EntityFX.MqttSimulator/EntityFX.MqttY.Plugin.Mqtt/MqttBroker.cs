using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.BrokerProfile;
using EntityFX.MqttY.Contracts.Mqtt.Formatters;
using EntityFX.MqttY.Contracts.Mqtt.Packets;
using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Plugin.Mqtt.Admission;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Plugin.Mqtt.Counter;
using EntityFX.MqttY.Plugin.Mqtt.Internals;
using System.Collections.Concurrent;
using EntityFX.MqttY.Contracts.Mqtt.Diagnostics;

namespace EntityFX.MqttY.Plugin.Mqtt
{
    internal class MqttBroker : Server, IMqttBroker, IMeasurementSource, IMqttBrokerMetricsSink, IPublisherMeasurementSource, IMqttPendingDiagnostics
    {
        public MqttPendingSnapshot GetPendingSnapshot(int detailLimit = 32, int sessionLimit = 64) =>
            Diagnostics.MqttPendingSnapshotBuilder.Capture(_sessionRepository.ReadAll(), _processingQueue.Count, detailLimit, sessionLimit);
        protected override bool ShouldTrackResponse(INetworkPacket packet) => false;
        private readonly IRepository<ClientSession> _sessionRepository = new InMemoryRepository<ClientSession>();

        // Test seam (S3): lets the characterization suite exercise the session
        // state machine without a full network. Production code keeps using the
        // repository through the protocol handlers only.
        internal IRepository<ClientSession> SessionRepository => _sessionRepository;

        private readonly MqttQos _maximumQualityOfService = MqttQos.ExactlyOnce;
        private readonly IMqttPacketManager _packetManager;
        private readonly IMqttTopicEvaluator _topicEvaluator;

        private readonly IBrokerAdmissionModel _admission;
        private readonly BrokerProcessingQueue _processingQueue = new();
        private readonly TimeSpan _tickPeriod;
        private readonly int _randomSeed;
        private readonly IBrokerMetricsSink _measurement = new BrokerMeasurement();

        // Cached, pre-sorted subscription snapshot. Rebuilding it on every
        // publish (ReadAll + SelectMany + Where + OrderBy + ToArray) dominates
        // broker fanout CPU. The snapshot is invalidated when sessions or
        // subscriptions change; order is preserved for deterministic fanout.
        private ClientSubscription[]? _subscriptionSnapshot;
        private int _subscriptionEpoch;
        private int _subscriptionSnapshotEpoch = -1;

        private void InvalidateSubscriptionSnapshot() => Interlocked.Increment(ref _subscriptionEpoch);

        private ClientSubscription[] GetSubscriptionSnapshot()
        {
            var epoch = Volatile.Read(ref _subscriptionEpoch);
            var snapshot = _subscriptionSnapshot;
            if (snapshot == null || _subscriptionSnapshotEpoch != epoch)
            {
                snapshot = _sessionRepository.ReadAll()
                    .SelectMany(s => s.GetSubscriptions())
                    .OrderBy(x => x.ClientId, StringComparer.Ordinal)
                    .ThenBy(x => x.TopicFilter, StringComparer.Ordinal)
                    .ToArray();
                _subscriptionSnapshot = snapshot;
                _subscriptionSnapshotEpoch = epoch;
            }
            return snapshot;
        }

        public override NodeType NodeType => NodeType.Server;

        public override bool IsQuiescent => base.IsQuiescent && _processingQueue.IsEmpty &&
            _sessionRepository.ReadAll().All(session =>
                !session.HasPendingMessages() &&
                !session.HasPendingAcknowledgements(excludePublishComplete: true));

        private readonly PacketIdProvider _packetIdProvider = new();

        protected readonly MqttCounters MqttCounters;

        public BrokerMetricsSnapshot GetMetrics()
        {
            var snapshot = _measurement.Snapshot(NetworkSimulator?.TotalTicks ?? 0, _tickPeriod);
            MqttCounters.ApplyMeasurement(snapshot);
            return snapshot;
        }

        public void ResetMeasurement(long startTick) => _measurement.Reset(startTick);

        public void TrackPublishers(IEnumerable<string> names) => _measurement.TrackPublishers(names);
        public BrokerQosMetricsSnapshot GetPublisherMetrics(string name, MqttQos qos) =>
            _measurement.PublisherSnapshot(name, qos, NetworkSimulator?.TotalTicks ?? 0, _tickPeriod);

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
            MqttBrokerProfile? brokerProfile = null, int randomSeed = 0, int? profileClientCount = null
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
                    InvalidateSubscriptionSnapshot();
                }

                UpdateProfileFromClients();
            };
            this._packetManager = packetManager;
            this._topicEvaluator = mqttTopicEvaluator;

            MqttCounters = new MqttCounters(Name, Name.Substring(0, 2), "MqttBroker", "MB", ticksOptions, enableCounters);
            counters.AddCounter(MqttCounters);

            _tickPeriod = ticksOptions.TickPeriod;
            _randomSeed = randomSeed;
            if (profileClientCount is <= 0) throw new ArgumentOutOfRangeException(nameof(profileClientCount));

            if (brokerProfile != null)
            {
                brokerProfile.Validate();
                _admission = new ProfileBrokerAdmissionModel(
                    brokerProfile, ticksOptions.TickPeriod, randomSeed, Name, profileClientCount,
                    () => EmpiricalProtocolFloorTicks,
                    () => EmpiricalCapacityPacing);
            }
            else
            {
                _admission = NoopBrokerAdmissionModel.Instance;
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

            var identity = packet.Context as PublishIdentity;
            var localPublishSequence = identity?.Sequence ?? (packet.Context is long sequence ? sequence : packet.Id);
            var tracker = _measurement.BeginAttempt(packet.From, publishPacket.QualityOfService,
                publishPacket.PacketId, packet.Id, identity?.StartedTick ?? NetworkSimulator!.TotalTicks,
                localPublishSequence, publishPacket.Payload.Length);
            if (tracker.Admitted)
            {
                ProcessFromClientPublishCore(packet, publishPacket);
                return;
            }

            var admission = _admission.TryAdmit(new PublishAdmissionContext(
                packet.From,
                publishPacket.QualityOfService,
                publishPacket.PacketId,
                publishPacket.Payload.Length,
                localPublishSequence,
                NetworkSimulator!.TotalTicks,
                GetServerClients().Count()));

            if (!admission.Admit)
            {
                switch (admission.RejectReason)
                {
                    case BrokerRejectReason.RateLimit:
                        MqttCounters.RefuseByRateLimit(publishPacket.QualityOfService);
                        _measurement.RejectByRate(tracker);
                        break;
                    case BrokerRejectReason.PublishFailure:
                        MqttCounters.RefuseByFailRate(publishPacket.QualityOfService);
                        _measurement.FailPublish(tracker);
                        break;
                    default:
                        throw new InvalidOperationException("Admission rejected without a reason.");
                }

                FailClientPublish(packet.From, publishPacket.PacketId);
                return;
            }

            tracker.ConditionalDeliveryLossRate = admission.ConditionalDeliveryLossRate;
            _measurement.Admit(tracker);

            if (admission.Immediate)
            {
                // No profile: keep the legacy immediate path without a processing queue.
                ProcessFromClientPublishCore(packet, publishPacket);
                return;
            }

            _processingQueue.Enqueue(packet, admission.DelayTicks);
        }

        private void FailClientPublish(string publisher, ushort? packetId)
        {
            if (packetId is ushort id &&
                NetworkSimulator!.GetNode(publisher, NodeType.Client) is IMqttClientPublishStateSink sink)
                sink.FailPublish(id);
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
                // Inbound and outbound packet-id namespaces are independent. A
                // repeated PUBLISH (active flow or completed marker) only repeats
                // PUBREC; a non-DUP PUBLISH may legitimately reuse the id.
                session.TryStartInboundPublish(
                    PendingMessage.FromPublish(publishPacket, PendingMessageStatus.AwaitingPublishRelease),
                    publishPacket.Duplicated);
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
            // Snapshot is pre-sorted by (ClientId, TopicFilter); filtering it in
            // place keeps the deterministic fanout order without re-sorting and
            // without per-publish allocations.
            var subscriptions = GetSubscriptionSnapshot();

            // QoS0 fanout delivers byte-identical packets to every subscriber of
            // one publish; serialize once and hand out the same read-only array (R3).
            byte[]? sharedQoS0Payload = null;

            foreach (var subscription in subscriptions)
            {
                if (!_topicEvaluator.Matches(publishPacket.Topic, subscription.TopicFilter))
                {
                    continue;
                }

                ProcessToClientPublish(packet, subscription, publishPacket, tracker, ref sharedQoS0Payload);
            }

            MqttCounters.IncrementPublish(publishPacket.QualityOfService);
            _measurement.CompleteFanOut(tracker);
        }

        private bool ProcessToClientPublish(
            INetworkPacket packet, ClientSubscription subscription, PublishPacket? publishPacket,
            BrokerMeasurement.PublishTracker? tracker, ref byte[]? sharedQoS0Payload)
        {
            if (publishPacket == null)
            {
                return false;
            }

            var supportedQos = publishPacket.QualityOfService > subscription.MaximumQualityOfService
                ? subscription.MaximumQualityOfService
                : publishPacket.QualityOfService;

            // QoS0 fanout delivers byte-identical packets to every subscriber of
            // one publish: serialize once per publish and distribute the same
            // read-only byte[] (R3). QoS1/2 keep per-subscriber packet ids.
            PublishPacket? subscriptionPublish = null;
            byte[] payloadBytes;
            if (supportedQos == MqttQos.AtMostOnce && sharedQoS0Payload != null)
            {
                payloadBytes = sharedQoS0Payload;
                subscriptionPublish = new PublishPacket(publishPacket.Topic, supportedQos, publishPacket.Retain, duplicated: false, packetId: null)
                {
                    Payload = publishPacket.Payload
                };
            }
            else
            {
                ushort? packetId = supportedQos == MqttQos.AtMostOnce ? null : (ushort?)_packetIdProvider.GetPacketId();
                var retain = publishPacket.Retain;
                subscriptionPublish = new PublishPacket(publishPacket.Topic, supportedQos, retain, duplicated: false, packetId: packetId)
                {
                    Payload = publishPacket.Payload
                };
                payloadBytes = _packetManager.PacketToBytes(subscriptionPublish) ?? Array.Empty<byte>();

                if (supportedQos == MqttQos.AtMostOnce)
                {
                    sharedQoS0Payload = payloadBytes;
                }
            }

            var packetPayload = GetPacket(
                NetworkSimulator!.GetPacketId(), subscription.ClientId, NodeType.Client, packet.FromIndex,
                    payloadBytes, ProtocolType, "MQTT Publish");
            _measurement.ExpectDelivery(tracker, packetPayload.Id);

            if (tracker != null && tracker.ConditionalDeliveryLossRate > 0 && DeterministicMqttSampler.Uniform(
                    _randomSeed, Name, tracker.Publisher, tracker.LocalPublishSequence,
                    $"delivery-loss:{subscription.ClientId}") < tracker.ConditionalDeliveryLossRate)
            {
                _measurement.DeliveryDropped(packetPayload.Id);
                return true;
            }

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

            // PUBACK only consumes the outbound QoS1 flow, never an inbound
            // QoS2 record that happens to use the same packet id.
            session.TryAcknowledgeOutbound(publishAckPacket.PacketId, out _);

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

            session.AddOutboundPending(PendingMessage.FromPublish(message, status));

            _sessionRepository.Update(session);
        }

        private void SendPublishAck(INetworkPacket packet, string clientId, MqttQos qos, PublishPacket publishPacket)
        {
            var ack = new PublishAckPacket(publishPacket.PacketId ?? 0);
            var ackPayload = _packetManager.PacketToBytes(ack) ?? Array.Empty<byte>();
            var reversePacket = NetworkSimulator!.GetReversePacket(packet, ackPayload, "MQTT PubAck");
            if (NetworkSimulator.EnableMonitoring)
            {
                var scope = NetworkSimulator.Monitoring.WithBeginScope(NetworkSimulator.TotalTicks, ref reversePacket,
                    $"Publish Ack {packet.From} to {packet.To} with topic {publishPacket.Topic}");
                NetworkSimulator.Monitoring.Push(NetworkSimulator.TotalTicks, packet, NetworkLoggerType.Send,
                    $"Send MQTT publish ack {packet.From} to {packet.To} with {publishPacket.Topic} (QoS={publishPacket.QualityOfService})", ProtocolType, "MQTT PubAck");
                Send(reversePacket);
                NetworkSimulator.Monitoring.WithEndScope(NetworkSimulator.TotalTicks, ref reversePacket);
            }
            else
            {
                Send(reversePacket);
            }
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

            if (!session.TryAdvanceOutboundQos2(publishReceivedPacket.PacketId, out _))
            {
                return;
            }

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

            // A duplicate PUBREL finds no active inbound flow and only repeats
            // PUBCOMP, so it can never fan a message out twice.
            var completed = session.TryCompleteInboundPublish(
                publishReleasePacket.PacketId, NetworkSimulator!.TotalTicks, out var pendingMessage);

            PublishPacket? publishPacket = null;
            if (completed && pendingMessage != null)
            {
                publishPacket = pendingMessage.ToPublishPacket();
                _sessionRepository.Update(session);
            }

            SendPublishComplete(packet, publishReleasePacket.PacketId);
            if (publishPacket != null)
                FanOutAndCount(packet, publishPacket);
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

            session.TryCompleteOutboundQos2(publishCompletePacket.PacketId, out _);
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

                    if (session.UpsertSubscription(clientId, subscription.TopicFilter,
                            subscription.MaximumQualityOfService))
                    {
                        InvalidateSubscriptionSnapshot();
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


            if (NetworkSimulator!.EnableMonitoring)
            {
                NetworkSimulator!.Monitoring.Push(NetworkSimulator.TotalTicks, packet, NetworkLoggerType.Send,
                    $"Send MQTT subscribe ack {packet.From} to {packet.To}", ProtocolType, "MQTT SubAck");
            }
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
                InvalidateSubscriptionSnapshot();
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
            PruneCompletedAcknowledgements();

            foreach (var packet in _processingQueue.DrainReady())
            {
                var publishPacket = _packetManager.BytesToPacket<PublishPacket>(packet.Payload);
                ProcessFromClientPublishCore(packet, publishPacket);
            }
        }

        private long _lastAcknowledgementPruneTick;

        private void PruneCompletedAcknowledgements()
        {
            var tick = NetworkSimulator!.TotalTicks;
            if (tick - _lastAcknowledgementPruneTick < 64)
            {
                return;
            }

            _lastAcknowledgementPruneTick = tick;
            foreach (var session in _sessionRepository.ReadAll())
            {
                session.PrunePendingAcknowledgements(tick);
            }
        }

        private void UpdateProfileFromClients()
        {
            _admission.ClearRateLimiters();
        }

        public long? EmpiricalProtocolFloorTicks { get; set; }
        public bool EmpiricalCapacityPacing { get; set; }

        public override void Clear()
        {
            PacketReceived -= MqttBroker_PacketReceived;
            _sessionRepository.Clear();
            _processingQueue.Clear();
            base.Clear();
        }
    }
}
