using EntityFX.MqttY.Contracts.Network;

namespace EntityFX.MqttY.Contracts.Utils;

public interface IFactory<TService, TApplicationOptions>
{
    TService Create(NodeBuildOptions<TApplicationOptions> options);

    TService Configure(NodeBuildOptions<TApplicationOptions> options, TService service);
}