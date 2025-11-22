namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    internal class ClientSession : IStorageObject
    {
        public ClientSession(string id, string clientId,  bool clean = false)
        {
            Id = id;
            ClientId = clientId;
            Clean = clean;
            Subscriptions = new List<ClientSubscription>();
            PendingMessages = new List<PendingMessage>();
            PendingAcknowledgements = new List<PendingAcknowledgement>();
        }

        public string Id { get; }
        
        public string ClientId { get; }

        public bool Clean { get; }

        public List<ClientSubscription> Subscriptions { get; set; }

        public List<PendingMessage> PendingMessages { get; set; }

        public List<PendingAcknowledgement> PendingAcknowledgements { get; set; }
    }
}
