using EntityFX.MqttY.Contracts.Monitoring;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetworkGraph
    {
        public IPathFinder PathFinder { get; }
        public IMonitoring Monitoring { get; }

        public IReadOnlyDictionary<string, INetwork> Networks { get; }

        INetwork? BuildNetwork(string address);

        IClient? BuildClient(string address, INetwork network);

        IServer? BuildServer(string address, INetwork network);

        ILeafNode? BuildNode(string address, NodeType nodeType);

        void RemoveNetwork(INetwork network);

        void RemoveClient(INetwork network);

        void RemoveServer(INetwork network);

        INetwork? GetNodeNetwork(string address, NodeType nodeType);

        ILeafNode? GetNode(string address, NodeType nodeType);
    }
}
