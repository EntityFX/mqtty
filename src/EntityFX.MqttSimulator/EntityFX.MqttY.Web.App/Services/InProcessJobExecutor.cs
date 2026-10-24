using System.Text.Json;
using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Engines.Hosting;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Исполнение задания внутри процесса ASP.NET Core через EngineHost.</summary>
public sealed class InProcessJobExecutor : IJobExecutor
{
    private readonly EngineHost _engineHost;

    public InProcessJobExecutor(EngineHost engineHost)
    {
        _engineHost = engineHost;
    }

    public async Task<ExperimentResult> ExecuteAsync(SimulationJob job, CancellationToken ct)
    {
        var request = new ExperimentRequest
        {
            Command = EngineCommand.Run,
            Options = JsonSerializer.SerializeToElement(job.RunOptions),
            OutputDirectory = job.OutputDir,
            Progress = job.Progress
        };

        var settings = new EngineRunSettings
        {
            TimeLimit = job.TimeLimit,
            ParallelExecution = job.Parallel,
            IsolatedProcess = false
        };

        return await _engineHost.RunAsync(job.Engine, request, settings, ct);
    }
}
