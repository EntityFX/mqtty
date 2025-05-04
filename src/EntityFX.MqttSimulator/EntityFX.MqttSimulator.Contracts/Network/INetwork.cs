namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetwork : ISender
    {
        IReadOnlyDictionary<string, INetwork> LinkedNearestNetworks { get; }
        IReadOnlyDictionary<string, IServer> Servers { get; }
        IReadOnlyDictionary<string, IClient> Clients { get; }
        IReadOnlyDictionary<string, IApplication> Applications { get; }
        
        IReadOnlyDictionary<string, INode> Nodes { get; }

        public string NetworkType { get; }

        long QueueSize {  get; }

        bool Link(INetwork network);

        bool Unlink(INetwork network);

        bool UnlinkAll();

        bool AddServer(IServer server);

        bool RemoveServer(string server);

        bool AddApplication(IApplication application);

        bool RemoveApplication(string application);

        bool AddClient(IClient client);

        bool RemoveClient(string clientAddress);

        INode? FindNode(string nodeAddress, NodeType type);
    }
}
