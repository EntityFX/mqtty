using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Factories;

internal class NetworkGraphFactoryOption
{
    public NetworkGraphOption NetworkGraphOption { get; init; } = new();

    public MonitoringOption MonitoringOption { get; init; } = new();
    public string OptionsPath { get; internal set; }
}
