using EntityFX.MqttY.Engines.Contracts;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Исполнитель задания: in-process или out-of-process.</summary>
public interface IJobExecutor
{
    Task<ExperimentResult> ExecuteAsync(SimulationJob job, CancellationToken ct);
}
