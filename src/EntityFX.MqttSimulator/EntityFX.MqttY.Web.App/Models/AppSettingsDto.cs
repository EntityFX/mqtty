namespace EntityFX.MqttY.Web.App.Models;

/// <summary>Настройки приложения (лимит времени, параллельность, режим исполнения).</summary>
public sealed class AppSettingsDto
{
    public TimeSpan TimeLimit { get; set; } = TimeSpan.FromMinutes(10);

    public bool Parallel { get; set; }

    public string ExecutionMode { get; set; } = "InProcess";

    public int MaxParallelJobs { get; set; } = 4;

    public string DataDirectory { get; set; } = "data";
}
