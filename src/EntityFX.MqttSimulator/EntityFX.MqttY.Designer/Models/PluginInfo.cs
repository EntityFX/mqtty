namespace EntityFX.MqttY.Designer.Models;

public class PluginInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IReadOnlyList<string> SupportedProtocols { get; set; } = Array.Empty<string>();
    public bool IsLoaded { get; set; }
}