using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

public class ActionClientBuilder : IClientBuilder
{
    private readonly ClientBuilderAction func;

    public ActionClientBuilder(ClientBuilderAction func)
    {
        this.func = func;
    }

    public IClient? BuildClient(int index, string name, string protocolType, string specification, INetwork network,
        TicksOptions ticks, bool enableCounters, string? group = null, 
        int? groupAmount = null, Dictionary<string, string[]>? additional = null)
    {
        return func.Invoke(index, name, protocolType, specification, network, ticks, enableCounters, group, groupAmount, additional);
    }

    public TClient? BuildClient<TClient>(int index, string name, string protocolType, string specification, INetwork network,
        TicksOptions ticks, bool enableCounters, string? group = null, 
        int? groupAmount = null, Dictionary<string, string[]>? additional = null) where TClient : IClient
    {
        return (TClient?)BuildClient(index, name, protocolType, specification, network,
            ticks, enableCounters, group, groupAmount, additional);
    }
}
