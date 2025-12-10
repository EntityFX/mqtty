namespace EntityFX.MqttY.Contracts.Options;

public class NetworkOptions
{
    public int Speed { get; set; }

    public int TransferTicks { get; set; }

    public string NetworkType { get; set; } = string.Empty;
}