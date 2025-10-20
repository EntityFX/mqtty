using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Contracts.Options;

public class NetworkGraphFactoryOption
{
    public NetworkGraphOption NetworkGraphOption { get; init; } = new();

    public MonitoringOption MonitoringOption { get; init; } = new();
    public TicksOptions TicksOption { get; init; } = new();

    public string OptionsPath { get; set; } = string.Empty;

    public INetworkSimulatorBuilder? NetworkSimulatorBuilder { get; init; }

    public IFactory<INetworkSimulator, NetworkGraphFactoryOption>? NetworkGraphFactory { get; init; }
}
