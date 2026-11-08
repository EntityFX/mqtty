namespace EntityFX.MqttY.Web.App.Security;

/// <summary>Optional web security switches; safe defaults keep local usage open.</summary>
public sealed class SecurityOptions
{
    public string? ApiKey { get; set; }
    public int RateLimitPerMinute { get; set; } = 60;
    public string[] CorsOrigins { get; set; } = Array.Empty<string>();
    public bool UseHttpsRedirection { get; set; }
    public bool UseHsts { get; set; }
    public int MaxImportBytes { get; set; } = 2 * 1024 * 1024;
}
