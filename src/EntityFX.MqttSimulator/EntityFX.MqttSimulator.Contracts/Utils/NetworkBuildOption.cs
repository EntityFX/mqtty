using EntityFX.MqttY.Contracts.Options;

namespace EntityFX.MqttY.Contracts.Utils;

public class NetworkBuildOption
{
    public NetworkTypeOption NetworkTypeOption { get; set; } = new();
    public TicksOptions TicksOptions { get; set; } = new();

    public Dictionary<string, string[]> Additional { get; set; } = new();
}