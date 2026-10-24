namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Управление заданиями симуляций: постановка в очередь, остановка, пауза/возобновление.</summary>
public interface ISimulationJobRunner
{
    Task<string> EnqueueAsync(SimulationJob job);

    Task StopAsync(string id);

    Task PauseAsync(string id);

    Task ResumeAsync(string id);
}
