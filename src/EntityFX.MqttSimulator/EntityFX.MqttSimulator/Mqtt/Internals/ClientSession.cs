namespace EntityFX.MqttY.Mqtt.Internals
{
    internal class ClientSession
    {
        public ClientSession(string clientId, bool clean = false)
        {
            Id = clientId;
            Clean = clean;
            Subscriptions = new List<ClientSubscription>();
            PendingMessages = new List<PendingMessage>();
            PendingAcknowledgements = new List<PendingAcknowledgement>();
        }

        public string Id { get; }

        public bool Clean { get; }

        public List<ClientSubscription> Subscriptions { get; set; }

        public List<PendingMessage> PendingMessages { get; set; }

        public List<PendingAcknowledgement> PendingAcknowledgements { get; set; }
    }
}
