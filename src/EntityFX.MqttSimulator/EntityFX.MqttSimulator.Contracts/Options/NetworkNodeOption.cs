namespace EntityFX.MqttY.Contracts.Options;

public class NetworkNodeOption
{
    public NetworkLinkOption[] Links { get; set; } = Array.Empty<NetworkLinkOption>();
    public int Index { get; set; }
}