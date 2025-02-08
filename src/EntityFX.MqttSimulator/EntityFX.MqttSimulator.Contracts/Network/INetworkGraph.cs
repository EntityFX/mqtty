using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetworkGraph
    {
        public IPathFinder PathFinder { get; }
        public IMonitoring Monitoring { get; }

        public IReadOnlyDictionary<string, INetwork> Networks { get; }

        INetwork? BuildNetwork(string networkAddress);

        IClient? BuildClient(string clientAddress, string protocolType, INetwork network);

        string GetFullName(string clientAddress, string protocolType, string networkAddress);

        IServer? BuildServer(string serverAddress, string protocolType, INetwork network);

        ILeafNode? BuildNode(string address, NodeType nodeType);

        void RemoveNetwork(string networkAddress);

        void RemoveClient(string clientAddress);

        void RemoveServer(string serverAddress);

        INetwork? GetNetworkByNode(string nodeAddress, NodeType nodeType);

        ILeafNode? GetNode(string nodeAddress, NodeType nodeType);

        void Configure(NetworkGraphOptions value);
    }
}
