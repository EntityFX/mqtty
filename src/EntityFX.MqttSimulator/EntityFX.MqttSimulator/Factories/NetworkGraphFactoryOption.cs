using EntityFX.MqttY.Contracts.Network;
using EntityFX.MqttY.Contracts.Options;
using EntityFX.MqttY.Contracts.Utils;

namespace EntityFX.MqttY.Factories;

internal class NetworkGraphFactoryOption
{
    public NetworkGraphOption NetworkGraphOption { get; init; } = new();

    public MonitoringOption MonitoringOption { get; init; } = new();
    public TicksOptions TicksOption { get; init; } = new();

    public string OptionsPath { get; internal set; } = string.Empty;

    public INetworkSimulatorBuilder? NetworkSimulatorBuilder { get; init; }

    public IFactory<INetworkSimulator, NetworkGraphFactoryOption>? NetworkGraphFactory { get; init; }
}
