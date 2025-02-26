using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Utils;

public interface IFactory<TService, in TOptions, TApplicationOptions>
{
    TService Create(TOptions options);

    TService Configure(NodeBuildOptions<TApplicationOptions> options, TService service);
}