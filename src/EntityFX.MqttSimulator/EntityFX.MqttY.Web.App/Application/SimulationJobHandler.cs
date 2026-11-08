using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;

namespace EntityFX.MqttY.Web.App.Application;

/// <summary>Pause/resume/stop/restart/delete lifecycle operations for a job.</summary>
public sealed class SimulationJobHandler
{
    private readonly IJobRegistry _registry;
    private readonly ISimulationJobRunner _runner;

    public SimulationJobHandler(IJobRegistry registry, ISimulationJobRunner runner)
    {
        _registry = registry;
        _runner = runner;
    }

    public async Task<ApplicationResult<SimulationStatusDto>> PauseAsync(string id)
    {
        try
        {
            await _runner.PauseAsync(id);
        }
        catch (InvalidOperationException error)
        {
            return ApplicationResult<SimulationStatusDto>.Conflict(error.Message);
        }

        return ApplicationResult<SimulationStatusDto>.Ok(
            SimulationDtoMapper.ToStatusDto(_registry.Get(id)));
    }

    public async Task<ApplicationResult<SimulationStatusDto>> ResumeAsync(string id)
    {
        try
        {
            await _runner.ResumeAsync(id);
        }
        catch (InvalidOperationException error)
        {
            return ApplicationResult<SimulationStatusDto>.Conflict(error.Message);
        }

        return ApplicationResult<SimulationStatusDto>.Ok(
            SimulationDtoMapper.ToStatusDto(_registry.Get(id)));
    }

    public async Task<ApplicationResult<SimulationStatusDto>> StopAsync(string id)
    {
        await _runner.StopAsync(id);
        return ApplicationResult<SimulationStatusDto>.Ok(
            SimulationDtoMapper.ToStatusDto(_registry.Get(id)));
    }

    public async Task<ApplicationResult<SimulationStatusDto>> RestartAsync(string id)
    {
        var original = _registry.Get(id);
        if (original == null) return ApplicationResult<SimulationStatusDto>.NotFound();

        if (original.State is "Running" or "Paused" or "Queued" or "Cancelling")
        {
            return ApplicationResult<SimulationStatusDto>.Conflict(
                "Сначала завершите или остановите текущий запуск.");
        }

        var copy = new SimulationJob
        {
            TopologyId = original.TopologyId,
            Name = original.Name,
            Engine = original.Engine,
            RunOptions = original.RunOptions,
            TimeLimit = original.TimeLimit,
            Parallel = original.Parallel,
            ExecutionMode = original.ExecutionMode
        };

        try
        {
            await _runner.EnqueueAsync(copy);
        }
        catch (InvalidOperationException error)
        {
            return ApplicationResult<SimulationStatusDto>.Conflict(error.Message);
        }

        return ApplicationResult<SimulationStatusDto>.Accepted(SimulationDtoMapper.ToStatusDto(copy));
    }

    public ApplicationResult<bool> Delete(string id)
    {
        var job = _registry.Get(id);
        if (job == null) return ApplicationResult<bool>.NotFound();

        if (job.State is "Running" or "Cancelling" or "Queued" or "Paused")
        {
            return ApplicationResult<bool>.Conflict(
                "Stop the job and wait for terminal status before deleting its registry entry. " +
                "Scientific output is retained.");
        }

        _registry.Remove(id);
        return ApplicationResult<bool>.NoContent();
    }
}
