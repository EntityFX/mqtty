using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Contracts.Utils;

public class NetworkBuildOption
{
    public NetworkOptions? NetworkTypeOption { get; set; } = new();
    public TicksOptions? TicksOptions { get; set; } = new();
    public bool EnableCounters { get; set; }

    public Dictionary<string, string[]>? Additional { get; set; } = new();
}