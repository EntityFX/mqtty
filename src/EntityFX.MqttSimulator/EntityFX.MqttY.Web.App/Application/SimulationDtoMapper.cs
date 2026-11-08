using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;

namespace EntityFX.MqttY.Web.App.Application;

public static class SimulationDtoMapper
{
    public static SimulationStatusDto ToStatusDto(SimulationJob? job)
    {
        if (job == null)
        {
            return new SimulationStatusDto { State = "NotFound" };
        }

        return new SimulationStatusDto
        {
            Id = job.Id,
            TopologyId = job.TopologyId,
            Name = job.Name,
            TickMilliseconds = job.RunOptions?.Graph?.Ticks?.TickPeriod.TotalMilliseconds ?? 0,
            Engine = job.Engine,
            State = job.State,
            TotalTicks = job.TotalTicks,
            Step = job.Step,
            Errors = job.Errors,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt,
            OutputDir = job.OutputDir,
            Error = job.Error,
            ProcessId = job.ProcessId,
            ProcessWorkingSetBytes = job.ProcessWorkingSetBytes,
            ProcessCpuSeconds = job.ProcessCpuSeconds,
            LastObservedAt = job.LastObservedAt,
            Phase = job.Phase
        };
    }
}
