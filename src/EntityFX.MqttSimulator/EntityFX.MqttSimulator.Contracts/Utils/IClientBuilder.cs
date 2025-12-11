using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Contracts.Utils
{
    public interface IClientBuilder
    {
        IClient? BuildClient(int index, string name, string protocolType, string specification,
            INetwork network, TicksOptions ticks,
            string? group = null, int? groupAmount = null,
            Dictionary<string, string[]>? additional = null);

        TClient? BuildClient<TClient>(int index, string name, string protocolType, string specification,
            INetwork network, TicksOptions ticks,
            string? group = null, int? groupAmount = null,
            Dictionary<string, string[]>? additional = null)
            where TClient : IClient;

    }
}
