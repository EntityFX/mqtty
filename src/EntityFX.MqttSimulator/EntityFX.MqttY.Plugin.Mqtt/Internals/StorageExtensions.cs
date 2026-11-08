namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    /// <summary>
    /// Compatibility surface over <see cref="ClientSession"/> for diagnostics and
    /// tests. Protocol code uses the direction-aware session methods directly.
    /// </summary>
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

        public static IEnumerable<ClientSubscription> GetSubscriptions(this ClientSession session) =>
            session.GetSubscriptions();

        public static void AddSubscription(this ClientSession session, ClientSubscription subscription) =>
            session.AddSubscription(subscription);

        public static void RemoveSubscription(this ClientSession session, ClientSubscription subscription) =>
            session.RemoveSubscription(subscription);

        public static IEnumerable<PendingMessage> GetPendingMessages(this ClientSession session) =>
            session.GetPendingMessages();

        /// <summary>Allocation-free pending check used on every publisher tick.</summary>
        public static bool HasPendingMessages(this ClientSession session) =>
            session.HasPendingMessages();

        /// <summary>Allocation-free check: returns true if any pending message matches the predicate.</summary>
        public static bool AnyPendingMessage(this ClientSession session, Func<PendingMessage, bool> predicate) =>
            session.AnyPendingMessage(predicate);

        /// <summary>Allocation-free lookup for a pending acknowledgement matching the predicate.</summary>
        public static PendingAcknowledgement? FindPendingAcknowledgement(this ClientSession session,
            Func<PendingAcknowledgement, bool> predicate) => session.FindPendingAcknowledgement(predicate);

        public static void AddPendingMessage(this ClientSession session, PendingMessage pending) =>
            session.AddPendingMessage(pending);

        public static void RemovePendingMessage(this ClientSession session, PendingMessage? pending) =>
            session.RemovePendingMessage(pending);

        public static IEnumerable<PendingAcknowledgement> GetPendingAcknowledgements(this ClientSession session) =>
            session.GetPendingAcknowledgements();

        /// <summary>Allocation-free quiescence check; optionally ignores PublishComplete records.</summary>
        public static bool HasPendingAcknowledgements(this ClientSession session,
            bool excludePublishComplete = false) =>
            session.HasPendingAcknowledgements(excludePublishComplete);

        public static void AddPendingAcknowledgement(this ClientSession session,
            PendingAcknowledgement pending) => session.AddPendingAcknowledgement(pending);

        public static void RemovePendingAcknowledgement(this ClientSession session,
            PendingAcknowledgement? pending) => session.RemovePendingAcknowledgement(pending);

        public static void PrunePendingAcknowledgements(this ClientSession session, long tick) =>
            session.PrunePendingAcknowledgements(tick);
    }
}
