using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.NetworkLogger;
using System.Collections.Immutable;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetworkSimulator : IWithCounters
    {       
        public IPathFinder PathFinder { get; }
        public INetworkLogger Monitoring { get; }

        public IImmutableDictionary<string, INetwork> Networks { get; }
        public IImmutableDictionary<string, IClient> Clients { get; }
        public IImmutableDictionary<string, IServer> Servers { get; }

        public event EventHandler<Exception>? OnError;

        public event EventHandler<long>? OnRefresh;

        string GetAddress(string name, string protocolType, string network);

        bool AddClient(IClient client);

        bool AddServer(IServer server);

        bool AddApplication(IApplication application);

        bool AddNetwork(INetwork server);

        void RemoveNetwork(string name);

        void RemoveClient(string name);

        void RemoveServer(string name);

        bool Link(string sourceNetwork, string destinationNetwork);

        void UpdateRoutes();

        INetwork? GetNetworkByNode(string name, NodeType nodeType);

        ILeafNode? GetNode(string name, NodeType nodeType);

        NetworkPacket GetReversePacket(NetworkPacket packet, byte[] payload, string? category);

        bool Refresh(bool parallel);

        bool RefreshWithCounters(bool parallel);

        bool Reset();

        Task StartPeriodicRefreshAsync();

        void StopPeriodicRefresh();

        void Tick();

        long TotalTicks { get; }

        void AddCounterValue<TValue>(string name, TValue value)
            where TValue : struct, IEquatable<TValue>;
    }
}
