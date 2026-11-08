using EntityFX.MqttY.Contracts.Mqtt;
using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal enum PendingFlowDirection
    {
        Outbound,
        Inbound
    }

    /// <summary>
    /// One MQTT session. Pending protocol state is kept in two separate
    /// namespaces: <c>Outbound</c> (this endpoint sent a PUBLISH and waits for
    /// PUBACK/PUBREC/PUBCOMP) and <c>Inbound</c> (a peer sent a QoS2 PUBLISH and
    /// waits for PUBREL). Packet ids from the two directions are independent, so
    /// a PUBACK can never consume an inbound QoS2 record with the same id.
    ///
    /// The legacy <see cref="PendingMessages"/> snapshot is kept for diagnostics
    /// and compatibility; protocol handlers use the typed flow methods below.
    /// </summary>
    internal class ClientSession : IStorageObject
    {
        private readonly object _sync = new();
        private readonly object _subscriptionsSync = new();

        // Ordered for deterministic diagnostics; the dictionaries give O(1)
        // direction-aware protocol lookups.
        private readonly List<PendingMessage> _pendingOrder = new();
        private readonly Dictionary<ushort, PendingMessage> _outbound = new();
        private readonly Dictionary<ushort, PendingMessage> _inbound = new();
        private readonly List<PendingMessage> _unkeyed = new();
        private readonly List<ClientSubscription> _subscriptions = new();
        private readonly List<PendingAcknowledgement> _acknowledgements = new();

        public ClientSession(string id, string clientId, bool clean = false)
        {
            Id = id;
            ClientId = clientId;
            Clean = clean;
        }

        public string Id { get; }

        public string ClientId { get; }

        public bool Clean { get; }

        /// <summary>Compatibility snapshot for diagnostics. Do not mutate.</summary>
        public List<ClientSubscription> Subscriptions
        {
            get
            {
                lock (_subscriptionsSync)
                {
                    return new List<ClientSubscription>(_subscriptions);
                }
            }
        }

        /// <summary>Compatibility snapshot for diagnostics. Do not mutate.</summary>
        public List<PendingMessage> PendingMessages
        {
            get
            {
                lock (_sync)
                {
                    return new List<PendingMessage>(_pendingOrder);
                }
            }
        }

        /// <summary>Compatibility snapshot for diagnostics. Do not mutate.</summary>
        public List<PendingAcknowledgement> PendingAcknowledgements
        {
            get
            {
                lock (_sync)
                {
                    return new List<PendingAcknowledgement>(_acknowledgements);
                }
            }
        }

        public IEnumerable<ClientSubscription> GetSubscriptions()
        {
            lock (_subscriptionsSync)
            {
                return _subscriptions.ToArray();
            }
        }

        public ClientSubscription? FindSubscription(string topicFilter)
        {
            lock (_subscriptionsSync)
            {
                foreach (var subscription in _subscriptions)
                {
                    if (subscription.TopicFilter == topicFilter)
                    {
                        return subscription;
                    }
                }

                return null;
            }
        }

        /// <summary>Adds or updates a subscription. Returns true when it was created.</summary>
        public bool UpsertSubscription(string clientId, string topicFilter, MqttQos qos)
        {
            lock (_subscriptionsSync)
            {
                foreach (var subscription in _subscriptions)
                {
                    if (subscription.TopicFilter == topicFilter)
                    {
                        subscription.MaximumQualityOfService = qos;
                        return false;
                    }
                }

                _subscriptions.Add(new ClientSubscription
                {
                    ClientId = clientId,
                    TopicFilter = topicFilter,
                    MaximumQualityOfService = qos
                });
                return true;
            }
        }

        public void AddSubscription(ClientSubscription subscription)
        {
            lock (_subscriptionsSync)
            {
                _subscriptions.Add(subscription);
            }
        }

        public void RemoveSubscription(ClientSubscription subscription)
        {
            lock (_subscriptionsSync)
            {
                _subscriptions.Remove(subscription);
            }
        }

        public IEnumerable<PendingMessage> GetPendingMessages()
        {
            lock (_sync)
            {
                return _pendingOrder.ToArray();
            }
        }

        /// <summary>Allocation-free pending check used on every publisher tick.</summary>
        public bool HasPendingMessages()
        {
            lock (_sync)
            {
                return _pendingOrder.Count > 0;
            }
        }

        /// <summary>Allocation-free check for any pending message matching the predicate.</summary>
        public bool AnyPendingMessage(Func<PendingMessage, bool> predicate)
        {
            lock (_sync)
            {
                return _pendingOrder.Any(predicate);
            }
        }

        public void AddPendingMessage(PendingMessage pending)
        {
            if (pending.Status == PendingMessageStatus.AwaitingPublishRelease)
            {
                AddInboundPending(pending);
            }
            else
            {
                AddOutboundPending(pending);
            }
        }

        public void AddOutboundPending(PendingMessage pending)
        {
            lock (_sync)
            {
                AddPendingNoLock(pending, PendingFlowDirection.Outbound);
            }
        }

        public void AddInboundPending(PendingMessage pending)
        {
            lock (_sync)
            {
                AddPendingNoLock(pending, PendingFlowDirection.Inbound);
            }
        }

        /// <summary>
        /// Starts an inbound QoS2 flow. Returns false when the PUBLISH is a
        /// duplicate of an active or completed flow, in which case the caller
        /// must only repeat PUBREC. A non-DUP PUBLISH may reuse a completed
        /// packet id and opens a fresh flow.
        /// </summary>
        public bool TryStartInboundPublish(PendingMessage message, bool duplicated)
        {
            lock (_sync)
            {
                if (message.PacketId is not ushort packetId)
                {
                    AddPendingNoLock(message, PendingFlowDirection.Inbound);
                    return true;
                }

                if (_inbound.ContainsKey(packetId))
                {
                    return false;
                }

                if (HasCompletedInboundNoLock(packetId))
                {
                    if (duplicated)
                    {
                        return false;
                    }

                    RemoveCompletedInboundNoLock(packetId);
                }

                AddPendingNoLock(message, PendingFlowDirection.Inbound);
                return true;
            }
        }

        public bool TryGetInbound(ushort packetId, out PendingMessage? message)
        {
            lock (_sync)
            {
                return _inbound.TryGetValue(packetId, out message);
            }
        }

        /// <summary>
        /// Completes an inbound QoS2 flow on PUBREL: removes the stored PUBLISH,
        /// records the completed marker and returns the message to fan out/deliver.
        /// A duplicate PUBREL returns false but must still be acknowledged.
        /// </summary>
        public bool TryCompleteInboundPublish(ushort packetId, long tick, out PendingMessage? message)
        {
            lock (_sync)
            {
                if (!_inbound.TryGetValue(packetId, out message))
                {
                    return false;
                }

                RemovePendingNoLock(message);
                AddCompletedInboundNoLock(packetId, tick);
                return true;
            }
        }

        public bool HasCompletedInbound(ushort packetId)
        {
            lock (_sync)
            {
                return HasCompletedInboundNoLock(packetId);
            }
        }

        public void RemoveCompletedInbound(ushort packetId)
        {
            lock (_sync)
            {
                RemoveCompletedInboundNoLock(packetId);
            }
        }

        /// <summary>Consumes an outbound QoS1 delivery on PUBACK.</summary>
        public bool TryAcknowledgeOutbound(ushort packetId, out PendingMessage? message)
        {
            lock (_sync)
            {
                if (_outbound.TryGetValue(packetId, out message) &&
                    message.Status == PendingMessageStatus.PendingToAcknowledge)
                {
                    RemovePendingNoLock(message);
                    return true;
                }

                message = null;
                return false;
            }
        }

        /// <summary>
        /// Advances an outbound QoS2 flow on PUBREC. A repeated PUBREC is still
        /// answered with PUBREL, so an already-awaiting-complete flow returns true.
        /// </summary>
        public bool TryAdvanceOutboundQos2(ushort packetId, out PendingMessage? message)
        {
            lock (_sync)
            {
                if (!_outbound.TryGetValue(packetId, out message))
                {
                    return false;
                }

                if (message.Status is not (PendingMessageStatus.AwaitingPublishReceived or
                    PendingMessageStatus.AwaitingPublishComplete))
                {
                    message = null;
                    return false;
                }

                message.Status = PendingMessageStatus.AwaitingPublishComplete;
                return true;
            }
        }

        /// <summary>Completes an outbound QoS2 flow on PUBCOMP.</summary>
        public bool TryCompleteOutboundQos2(ushort packetId, out PendingMessage? message)
        {
            lock (_sync)
            {
                if (_outbound.TryGetValue(packetId, out message) &&
                    message.Status == PendingMessageStatus.AwaitingPublishComplete)
                {
                    RemovePendingNoLock(message);
                    return true;
                }

                message = null;
                return false;
            }
        }

        /// <summary>Drops any outbound flow with the packet id (publish failure/cleanup).</summary>
        public bool TryRemoveOutbound(ushort packetId, out PendingMessage? message)
        {
            lock (_sync)
            {
                if (!_outbound.TryGetValue(packetId, out message))
                {
                    message = null;
                    return false;
                }

                RemovePendingNoLock(message);
                return true;
            }
        }

        public void RemovePendingMessage(PendingMessage? pending)
        {
            if (pending == null)
            {
                return;
            }

            lock (_sync)
            {
                RemovePendingNoLock(pending);
            }
        }

        public IEnumerable<PendingAcknowledgement> GetPendingAcknowledgements()
        {
            lock (_sync)
            {
                return _acknowledgements.ToArray();
            }
        }

        public PendingAcknowledgement? FindPendingAcknowledgement(Func<PendingAcknowledgement, bool> predicate)
        {
            lock (_sync)
            {
                return _acknowledgements.FirstOrDefault(predicate);
            }
        }

        /// <summary>Allocation-free quiescence check; optionally ignores PublishComplete records.</summary>
        public bool HasPendingAcknowledgements(bool excludePublishComplete = false)
        {
            lock (_sync)
            {
                if (!excludePublishComplete)
                {
                    return _acknowledgements.Count > 0;
                }

                return _acknowledgements.Any(ack => ack.Type != MqttPacketType.PublishComplete);
            }
        }

        public void AddPendingAcknowledgement(PendingAcknowledgement pending)
        {
            lock (_sync)
            {
                _acknowledgements.Add(pending);
            }
        }

        public void RemovePendingAcknowledgement(PendingAcknowledgement? pending)
        {
            if (pending == null)
            {
                return;
            }

            lock (_sync)
            {
                _acknowledgements.Remove(pending);
            }
        }

        public bool RemovePendingAcknowledgement(MqttPacketType type, ushort packetId)
        {
            lock (_sync)
            {
                for (var i = 0; i < _acknowledgements.Count; i++)
                {
                    var acknowledgement = _acknowledgements[i];
                    if (acknowledgement.Type == type && acknowledgement.PacketId == packetId)
                    {
                        _acknowledgements.RemoveAt(i);
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Evicts completed QoS2 markers that are older than the retention window
        /// and enforces the per-session cap. Other acknowledgement types are kept.
        /// </summary>
        public void PrunePendingAcknowledgements(long tick)
        {
            var retention = StorageExtensions.CompletedAcknowledgementRetentionTicks;
            var capacity = StorageExtensions.MaxCompletedAcknowledgements;
            lock (_sync)
            {
                if (_acknowledgements.Count == 0)
                {
                    return;
                }

                _acknowledgements.RemoveAll(ack =>
                    ack.Type == MqttPacketType.PublishComplete && tick - ack.Tick >= retention);

                var completed = _acknowledgements.Count(ack => ack.Type == MqttPacketType.PublishComplete);
                var excess = completed - capacity;
                for (var i = 0; i < _acknowledgements.Count && excess > 0;)
                {
                    if (_acknowledgements[i].Type == MqttPacketType.PublishComplete)
                    {
                        _acknowledgements.RemoveAt(i);
                        excess--;
                    }
                    else
                    {
                        i++;
                    }
                }
            }
        }

        /// <summary>Number of packet ids duplicated inside one flow direction.</summary>
        public int CountDuplicatePendingMessageIds()
        {
            lock (_sync)
            {
                var inbound = _inbound.Count;
                var outbound = _outbound.Count;
                var keyed = _pendingOrder.Count(p => p.PacketId.HasValue);
                return Math.Max(0, keyed - inbound - outbound);
            }
        }

        private bool HasCompletedInboundNoLock(ushort packetId) =>
            _acknowledgements.Any(ack =>
                ack.Type == MqttPacketType.PublishComplete && ack.PacketId == packetId);

        private void RemoveCompletedInboundNoLock(ushort packetId) =>
            _acknowledgements.RemoveAll(ack =>
                ack.Type == MqttPacketType.PublishComplete && ack.PacketId == packetId);

        private void AddCompletedInboundNoLock(ushort packetId, long tick)
        {
            _acknowledgements.Add(new PendingAcknowledgement
            {
                Type = MqttPacketType.PublishComplete,
                PacketId = packetId,
                Tick = tick
            });
        }

        private void AddPendingNoLock(PendingMessage pending, PendingFlowDirection direction)
        {
            if (pending.PacketId is ushort packetId)
            {
                var dictionary = direction == PendingFlowDirection.Outbound ? _outbound : _inbound;
                if (dictionary.TryGetValue(packetId, out var existing))
                {
                    RemovePendingNoLock(existing);
                }

                dictionary[packetId] = pending;
            }
            else
            {
                _unkeyed.Add(pending);
            }

            _pendingOrder.Add(pending);
        }

        private bool RemovePendingNoLock(PendingMessage? pending)
        {
            if (pending == null)
            {
                return false;
            }

            var removed = _pendingOrder.Remove(pending);
            if (pending.PacketId is ushort packetId)
            {
                RemoveIfSame(_outbound, packetId, pending);
                RemoveIfSame(_inbound, packetId, pending);
            }
            else
            {
                removed |= _unkeyed.Remove(pending);
            }

            return removed;
        }

        private static void RemoveIfSame(Dictionary<ushort, PendingMessage> dictionary,
            ushort packetId, PendingMessage pending)
        {
            if (dictionary.TryGetValue(packetId, out var existing) && ReferenceEquals(existing, pending))
            {
                dictionary.Remove(packetId);
            }
        }
    }
}
