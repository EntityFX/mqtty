namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Параметры запуска внешнего CLI-процесса.</summary>
public sealed class OutOfProcessOptions
{
    public string Dll { get; set; } = string.Empty;

    public string ArgsTemplate { get; set; } = "{engine} run --config {config} --output {output}";
}
