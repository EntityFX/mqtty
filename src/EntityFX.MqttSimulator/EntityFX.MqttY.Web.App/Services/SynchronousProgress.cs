using EntityFX.MqttY.Engines.Contracts;
namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Не откладывает snapshots в ThreadPool после финального Completed.</summary>
public sealed class SynchronousProgress : IProgress<EngineProgress>
{
    private readonly Action<EngineProgress> _report;
    public SynchronousProgress(Action<EngineProgress> report) { _report = report; }
    public void Report(EngineProgress value) => _report(value);
}
