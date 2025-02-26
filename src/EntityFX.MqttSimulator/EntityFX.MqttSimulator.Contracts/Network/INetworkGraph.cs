using EntityFX.MqttY.Contracts.Monitoring;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetworkGraph
    {
        public IPathFinder PathFinder { get; }
        public IMonitoring Monitoring { get; }

        public IReadOnlyDictionary<string, INetwork> Networks { get; }

        INetwork? BuildNetwork(int index, string name, string address);

        IClient? BuildClient(int index, string name, string protocolType, INetwork network, string? group = null, int? groupAmount = null, 
            Dictionary<string, string[]>? additional = null);

        TClient? BuildClient<TClient>(int index, string name, string protocolType, INetwork network, string? group = null, int? groupAmount = null, 
            Dictionary<string, string[]>? additional = null)
            where TClient : IClient;

        IServer? BuildServer(int index, string name, string protocolType, INetwork network, string? group = null, int? groupAmount = null, 
            Dictionary<string, string[]>? additional = null);

        ILeafNode? BuildNode(int index, string name, string address, NodeType nodeType, string? group = null, int? groupAmount = null,
            Dictionary<string, string[]>? additional = null);

        IApplication? BuildApplication<TConfiguration>(int index, string name, string protocolType, INetwork network, string? group = null, int? groupAmount = null,
            TConfiguration? applicationConfig = default);

        string GetAddress(string name, string protocolType, string network);


        void RemoveNetwork(string name);

        void RemoveClient(string name);

        void RemoveServer(string name);

        INetwork? GetNetworkByNode(string name, NodeType nodeType);

        ILeafNode? GetNode(string name, NodeType nodeType);

        void Configure(NetworkGraphOptions value);

        Packet GetReversePacket(Packet packet, byte[] payload, string? category);

        void Refresh();

        void Tick(INode nodeBase);
    }
}
