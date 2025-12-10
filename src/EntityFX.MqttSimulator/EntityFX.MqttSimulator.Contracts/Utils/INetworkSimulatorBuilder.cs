using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Contracts.Utils
{
    public interface INetworkSimulatorBuilder
    {
        string? OptionsPath { get; set; }

        INetworkSimulator? NetworkSimulator { get; }

        INetwork? BuildNetwork(int index, string name, string address, 
            NetworkOptions networkTypeOption, TicksOptions ticks);

        IClient? BuildClient(int index, string name, string protocolType, string specification,
            INetwork network, NetworkOptions networkTypeOption, TicksOptions ticks, 
            string? group = null, int? groupAmount = null,
            Dictionary<string, string[]>? additional = null);

        TClient? BuildClient<TClient>(int index, string name, string protocolType, string specification,
            INetwork network, NetworkOptions networkTypeOption, TicksOptions ticks, 
            string? group = null, int? groupAmount = null,
            Dictionary<string, string[]>? additional = null)
            where TClient : IClient;

        IServer? BuildServer(int index, string name, string protocolType, string specification,
            INetwork network, NetworkOptions networkTypeOption, TicksOptions ticks, 
            string? group = null, int? groupAmount = null,
            Dictionary<string, string[]>? additional = null);

        ILeafNode? BuildNode(int index, string name, string address, NodeType nodeType, string? group = null, int? groupAmount = null,
            Dictionary<string, string[]>? additional = null);

        IApplication? BuildApplication(int index, string name, string protocolType, string specification,
            INetwork network, NetworkOptions? networkTypeOption, TicksOptions? ticks, 
            string? group = null, int? groupAmount = null,
            Dictionary<string, string[]>? additional = default);

        void Configure(INetworkSimulator networkSimulator, NetworkGraphOption option);
    }
}
