using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal static class StorageExtensions
    {
        private static int ReadLimit(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

        /// <summary>How long a completed QoS2 marker is kept to suppress retransmissions.</summary>
        internal static readonly int CompletedAcknowledgementRetentionTicks =
            ReadLimit("MQTTY_QOS2_COMPLETED_TTL", 4096);

        /// <summary>Hard cap per session; oldest completed markers are evicted first.</summary>
        internal static readonly int MaxCompletedAcknowledgements =
            ReadLimit("MQTTY_QOS2_COMPLETED_CAP", 4096);
        public static IEnumerable<ClientSubscription> GetSubscriptions(this ClientSession session)
        {
            lock (session.SubscriptionsSync)
            {
                return session.Subscriptions.ToArray();
            }
        }

        public static void AddSubscription(this ClientSession session, ClientSubscription subscription)
        {
            lock (session.SubscriptionsSync)
            {
                session.Subscriptions.Add(subscription);
            }
        }

        public static void RemoveSubscription(this ClientSession session, ClientSubscription subscription)
        {
            lock (session.SubscriptionsSync)
            {
                session.Subscriptions.Remove(subscription);
            }
        }

        public static IEnumerable<PendingMessage> GetPendingMessages(this ClientSession session)
        {
            lock (session.PendingMessagesSync)
            {
                return session.PendingMessages.ToArray();
            }
        }

        /// <summary>Allocation-free pending check used on every publisher tick.</summary>
        public static bool HasPendingMessages(this ClientSession session)
        {
            lock (session.PendingMessagesSync)
            {
                return session.PendingMessages.Count > 0;
            }
        }

        /// <summary>Allocation-free check: returns true if any pending message matches <paramref name="predicate"/>.
        /// Avoids the <see cref="GetPendingMessages"/> ToArray() copy in the hot ExactlyOnce publish path.</summary>
        public static bool AnyPendingMessage(this ClientSession session, Func<PendingMessage, bool> predicate)
        {
            lock (session.PendingMessagesSync)
            {
                foreach (var message in session.PendingMessages)
                {
                    if (predicate(message))
                        return true;
                }
                return false;
            }
        }

        /// <summary>Allocation-free lookup: returns the first pending acknowledgement matching <paramref name="predicate"/>.
        /// Avoids the <see cref="GetPendingAcknowledgements"/> ToArray() copy in the hot ExactlyOnce publish path.</summary>
        public static PendingAcknowledgement? FindPendingAcknowledgement(this ClientSession session,
            Func<PendingAcknowledgement, bool> predicate)
        {
            lock (session.PendingAcknowledgementsSync)
            {
                foreach (var acknowledgement in session.PendingAcknowledgements)
                {
                    if (predicate(acknowledgement))
                        return acknowledgement;
                }
                return null;
            }
        }

        public static void AddPendingMessage(this ClientSession session, PendingMessage pending)
        {
            lock (session.PendingMessagesSync)
            {
                session.PendingMessages.Add(pending);
            }
        }

        public static void RemovePendingMessage(this ClientSession session, PendingMessage? pending)
        {
            if (pending == null)
            {
                return;
            }
            lock (session.PendingMessagesSync)
            {
                session.PendingMessages.Remove(pending);
            }
        }

        public static IEnumerable<PendingAcknowledgement> GetPendingAcknowledgements(this ClientSession session)
        {
            lock (session.PendingAcknowledgementsSync)
            {
                return session.PendingAcknowledgements.ToArray();
            }
        }

        /// <summary>Allocation-free quiescence check; optionally ignores PublishComplete records.</summary>
        public static bool HasPendingAcknowledgements(this ClientSession session, bool excludePublishComplete = false)
        {
            lock (session.PendingAcknowledgementsSync)
            {
                if (!excludePublishComplete)
                {
                    return session.PendingAcknowledgements.Count > 0;
                }

                foreach (var acknowledgement in session.PendingAcknowledgements)
                {
                    if (acknowledgement.Type != MqttPacketType.PublishComplete)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static void AddPendingAcknowledgement(this ClientSession session, PendingAcknowledgement pending)
        {
            lock (session.PendingAcknowledgementsSync)
            {
                session.PendingAcknowledgements.Add(pending);
            }
        }

        public static void RemovePendingAcknowledgement(this ClientSession session, PendingAcknowledgement? pending)
        {
            if (pending == null)
            {
                return;
            }
            lock (session.PendingAcknowledgementsSync)
            {
                session.PendingAcknowledgements.Remove(pending);
            }
        }

        /// <summary>
        /// Evicts completed QoS2 markers that are older than the retention window
        /// and enforces the per-session cap. Other acknowledgement types are kept.
        /// </summary>
        public static void PrunePendingAcknowledgements(this ClientSession session, long tick)
        {
            var retention = CompletedAcknowledgementRetentionTicks;
            var capacity = MaxCompletedAcknowledgements;
            lock (session.PendingAcknowledgementsSync)
            {
                var acknowledgements = session.PendingAcknowledgements;
                if (acknowledgements.Count == 0)
                {
                    return;
                }

                acknowledgements.RemoveAll(ack =>
                    ack.Type == MqttPacketType.PublishComplete && tick - ack.Tick >= retention);

                var completed = 0;
                foreach (var acknowledgement in acknowledgements)
                {
                    if (acknowledgement.Type == MqttPacketType.PublishComplete)
                    {
                        completed++;
                    }
                }

                var excess = completed - capacity;
                for (var i = 0; i < acknowledgements.Count && excess > 0;)
                {
                    if (acknowledgements[i].Type == MqttPacketType.PublishComplete)
                    {
                        acknowledgements.RemoveAt(i);
                        excess--;
                    }
                    else
                    {
                        i++;
                    }
                }
            }
        }
    }
}
