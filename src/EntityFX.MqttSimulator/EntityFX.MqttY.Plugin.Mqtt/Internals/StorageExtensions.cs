namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal static class StorageExtensions
    {
        static readonly object SubscriptionsLock = new object();
        static readonly object PendingMessagesLock = new object();
        static readonly object PendingAcksLock = new object();

        public static IEnumerable<ClientSubscription> GetSubscriptions(this ClientSession session)
        {
            lock (SubscriptionsLock)
            {
                return session.Subscriptions.ToList();
            }
        }

        public static void AddSubscription(this ClientSession session, ClientSubscription subscription)
        {
            lock (SubscriptionsLock)
            {
                session.Subscriptions.Add(subscription);
            }
        }

        public static void RemoveSubscription(this ClientSession session, ClientSubscription subscription)
        {
            lock (SubscriptionsLock)
            {
                session.Subscriptions.Remove(subscription);
            }
        }

        public static IEnumerable<PendingMessage> GetPendingMessages(this ClientSession session)
        {
            lock (PendingMessagesLock)
            {
                return session.PendingMessages.ToList();
            }
        }

        public static void AddPendingMessage(this ClientSession session, PendingMessage pending)
        {
            lock (PendingMessagesLock)
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
            lock (PendingMessagesLock)
            {
                session.PendingMessages.Remove(pending);
            }
        }

        public static IEnumerable<PendingAcknowledgement> GetPendingAcknowledgements(this ClientSession session)
        {
            lock (PendingAcksLock)
            {
                return session.PendingAcknowledgements.ToList();
            }
        }

        public static void AddPendingAcknowledgement(this ClientSession session, PendingAcknowledgement pending)
        {
            lock (PendingAcksLock)
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
            lock (PendingAcksLock)
            {
                session.PendingAcknowledgements.Remove(pending);
            }
        }
    }
}
