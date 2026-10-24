namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Настройки приложения MqttY.Web (секция MqttYWeb в appsettings).</summary>
public sealed class DataDirectoryOptions
{
    public string DataDirectory { get; set; } = "data";
    public string? RequiredMountPoint { get; set; }
    public long MinimumFreeDiskBytes { get; set; } = 1024L * 1024 * 1024;

    public TimeSpan DefaultTimeLimit { get; set; } = TimeSpan.FromMinutes(10);

    public bool DefaultParallel { get; set; }

    public string DefaultExecutionMode { get; set; } = "InProcess";

    public int MaxParallelJobs { get; set; } = 4;

    public int MaxParallelJobLimit { get; set; } = 4;

    public int PollIntervalSeconds { get; set; } = 1;

    public OutOfProcessOptions OutOfProcess { get; set; } = new();
}
