using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.NetworkLogger;
using System.Collections.Immutable;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetworkSimulator : IWithCounters
    {
        public TimeSpan VirtualTime { get; }

        public TimeSpan RealTime { get; }


        bool Construction { get; set; }

        bool WaitMode { get; }

        bool EnableCounters { get; }

        public int CountNodes { get; }

        public long Errors { get; }

        IPathFinder PathFinder { get; }
        INetworkLogger Monitoring { get; }

        IDictionary<string, INetwork> Networks { get; }
        IDictionary<string, IClient> Clients { get; }
        IDictionary<string, IServer> Servers { get; }
        IDictionary<string, IApplication> Applications { get; }

        event EventHandler<Exception>? OnError;

        event EventHandler<long>? OnRefresh;

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

        long GetPacketId();

        INetworkPacket GetReversePacket(INetworkPacket packet, byte[] payload, string? category);

        bool Refresh(bool parallel, int strategy);

        bool RefreshWithCounters(bool parallel, int strategy);

        bool Reset();

        void Clear();

        Task StartPeriodicRefreshAsync();

        void StopPeriodicRefresh();

        void Tick();

        void Step();

        long TotalTicks { get; }

        long TotalSteps { get; }

        void AddCounterValue<TValue>(string name, string shortName, TValue value)
            where TValue : struct, IEquatable<TValue>;
    }
}
