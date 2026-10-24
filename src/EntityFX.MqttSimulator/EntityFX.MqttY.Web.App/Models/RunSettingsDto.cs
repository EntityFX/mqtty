namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Настройки исполнения симуляции.</summary>
public sealed class RunSettingsDto
{
    public TimeSpan? TimeLimit { get; set; }

    public bool Parallel { get; set; }

    public string ExecutionMode { get; set; } = string.Empty;
}
