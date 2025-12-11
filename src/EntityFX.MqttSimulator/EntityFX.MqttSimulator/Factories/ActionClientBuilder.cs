using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

public class ActionClientBuilder : IClientBuilder
{
    private readonly Func<(int index, string name, string protocolType, string specification, INetwork network, TicksOptions ticks, string? group, int? groupAmount, Dictionary<string, string[]>? additional), IClient> func;

    public ActionClientBuilder(Func<(int index, string name, string protocolType, string specification, INetwork network, TicksOptions ticks, string? group, int? groupAmount, Dictionary<string, string[]>? additional), IClient> func)
    {
        this.func = func;
    }

    public IClient? BuildClient(int index, string name, string protocolType, string specification, INetwork network, TicksOptions ticks, string? group = null, int? groupAmount = null, Dictionary<string, string[]>? additional = null)
    {
        return func.Invoke((index, name, protocolType, specification, network, ticks, group, groupAmount, additional));
    }

    public TClient? BuildClient<TClient>(int index, string name, string protocolType, string specification, INetwork network, TicksOptions ticks, string? group = null, int? groupAmount = null, Dictionary<string, string[]>? additional = null) where TClient : IClient
    {
        return (TClient?)BuildClient(index, name, protocolType, specification, network,
            ticks, group, groupAmount, additional);
    }
}
