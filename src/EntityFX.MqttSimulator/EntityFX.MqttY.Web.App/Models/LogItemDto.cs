namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Запись лога симуляции.</summary>
public sealed class LogItemDto
{
    public long Tick { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? Category { get; set; }
}
