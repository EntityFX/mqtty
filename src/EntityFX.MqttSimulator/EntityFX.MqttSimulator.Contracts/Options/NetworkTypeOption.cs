namespace EntityFX.MqttY.Contracts.Options;

public class NetworkTypeOption
{
    public int Speed { get; set; }

    public int RefreshTicks { get; set; }

    public int SendTicks { get; set; }

    public string NetworkType { get; set; } = string.Empty;
}