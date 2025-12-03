using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.NetworkLogger;
using System.Collections.Immutable;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetworkSimulator : IWithCounters
    {
        bool Construction { get; set; }

        IPathFinder PathFinder { get; }
        INetworkLogger Monitoring { get; }

        IImmutableDictionary<string, INetwork> Networks { get; }
        IImmutableDictionary<string, IClient> Clients { get; }
        IImmutableDictionary<string, IServer> Servers { get; }

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

        INetworkPacket GetReversePacket(INetworkPacket packet, byte[] payload, string? category);

        bool Refresh(bool parallel);

        bool RefreshWithCounters(bool parallel);

        bool Reset();

        Task StartPeriodicRefreshAsync();

        void StopPeriodicRefresh();

        void Tick();

        void Step();

        long TotalTicks { get; }

        long TotalSteps { get; }

        void AddCounterValue<TValue>(string name, TValue value)
            where TValue : struct, IEquatable<TValue>;
    }
}
