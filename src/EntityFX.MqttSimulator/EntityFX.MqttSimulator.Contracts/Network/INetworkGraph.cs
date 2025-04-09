using EntityFX.MqttY.Contracts.Counters;
using EntityFX.MqttY.Contracts.NetworkLogger;
using EntityFX.MqttY.Contracts.Options;
using System.Collections.Immutable;

namespace EntityFX.MqttY.Contracts.Network
{
    public interface INetworkGraph : IWithCounters
    {
        public string? OptionsPath { get; set; }
        
        public IPathFinder PathFinder { get; }
        public INetworkLogger Monitoring { get; }

        public IImmutableDictionary<string, INetwork> Networks { get; }

        public event EventHandler<Exception>? OnError;

        INetwork? BuildNetwork(int index, string name, string address, TicksOptions ticks);

        IClient? BuildClient(int index, string name, string protocolType, string specification,
            INetwork network, string? group = null, int? groupAmount = null, 
            Dictionary<string, string[]>? additional = null);

        TClient? BuildClient<TClient>(int index, string name, string protocolType, string specification,
            INetwork network, string? group = null, int? groupAmount = null, 
            Dictionary<string, string[]>? additional = null)
            where TClient : IClient;

        IServer? BuildServer(int index, string name, string protocolType, string specification,
            INetwork network, string? group = null, int? groupAmount = null, 
            Dictionary<string, string[]>? additional = null);

        ILeafNode? BuildNode(int index, string name, string address, NodeType nodeType, string? group = null, int? groupAmount = null,
            Dictionary<string, string[]>? additional = null);

        IApplication? BuildApplication<TConfiguration>(int index, string name, string protocolType, string specification,
            INetwork network, string? group = null, int? groupAmount = null,
            TConfiguration? applicationConfig = default);

        string GetAddress(string name, string protocolType, string network);


        void RemoveNetwork(string name);

        void RemoveClient(string name);

        void RemoveServer(string name);

        INetwork? GetNetworkByNode(string name, NodeType nodeType);

        ILeafNode? GetNode(string name, NodeType nodeType);

        void Configure(NetworkGraphOption value);

        NetworkPacket GetReversePacket(NetworkPacket packet, byte[] payload, string? category);

        bool Refresh();

        bool Reset();

        Task StartPeriodicRefreshAsync();

        void StopPeriodicRefresh();

        void Tick();
    }
}
