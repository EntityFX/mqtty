using EntityFX.MqttY.Contracts.Mqtt.Packets;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal static class StorageExtensions
    {
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
    }
}
