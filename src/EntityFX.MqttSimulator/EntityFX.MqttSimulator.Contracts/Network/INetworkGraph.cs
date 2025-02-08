using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetworkGraph
    {
        public IPathFinder PathFinder { get; }
        public IMonitoring Monitoring { get; }

        public IReadOnlyDictionary<string, INetwork> Networks { get; }

        INetwork? BuildNetwork(string name, string address);

        IClient? BuildClient(string name, string protocolType, INetwork network);

        TCLient? BuildClient<TCLient>(string name, string protocolType, INetwork network)
            where TCLient : IClient;

        IServer? BuildServer(string name, string protocolType, INetwork network);

        ILeafNode? BuildNode(string name, string address, NodeType nodeType);

        string GetAddress(string name, string protocolType, string network);


        void RemoveNetwork(string name);

        void RemoveClient(string name);

        void RemoveServer(string name);

        INetwork? GetNetworkByNode(string name, NodeType nodeType);

        ILeafNode? GetNode(string name, NodeType nodeType);

        void Configure(NetworkGraphOptions value);

        Packet GetReversePacket(Packet packet, byte[] payload);
    }
}
