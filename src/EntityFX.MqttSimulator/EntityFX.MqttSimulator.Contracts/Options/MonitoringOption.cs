namespace EntityFX.MqttY.Contracts.Options;

public class MonitoringOption
{
    public string Type { get; set; } = "console";

    public bool ScopesEnabled { get; set; }

    public string? Path { get; set; }

    public MonitoringIgnoreOption Ignore { get; set; } = new MonitoringIgnoreOption();
}
