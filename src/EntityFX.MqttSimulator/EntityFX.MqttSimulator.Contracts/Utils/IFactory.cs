using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Utils;

public interface IFactory<TService, in TOptions>
{
    TService Create(TOptions options);

    TService Configure(NodeBuildOptions options, TService service);
}