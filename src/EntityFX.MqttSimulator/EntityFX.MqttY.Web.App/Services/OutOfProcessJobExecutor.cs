using System.Diagnostics;
using System.Text.Json;
using EntityFX.MqttY.Engines.Contracts;
namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Запускает собственный worker без shell; cancellation завершает всё дерево процесса.</summary>
public sealed class OutOfProcessJobExecutor : IJobExecutor
{
    private readonly JsonFileStore _store;
    public OutOfProcessJobExecutor(JsonFileStore store) { _store = store; }
    public async Task<ExperimentResult> ExecuteAsync(SimulationJob job, CancellationToken ct)
    {
        var request = _store.Resolve($"workers/{job.Id}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(request)!);
        await File.WriteAllTextAsync(request, JsonSerializer.Serialize(job, SimulationWorker.Json), ct);
        var assembly = typeof(SimulationWorker).Assembly.Location;
        var info = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        info.ArgumentList.Add(assembly);
        info.ArgumentList.Add("--execute-simulation");
        info.ArgumentList.Add(request);
        using var process = Process.Start(info) ?? throw new IOException("Worker could not be started.");
        job.ProcessId = process.Id;
        job.Phase = "construct";
        // Drain both pipes immediately; retained output goes to data disk, not an unbounded string buffer.
        await using var stdout = File.Create(request + ".stdout.log");
        await using var stderr = File.Create(request + ".stderr.log");
        var copying = Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(stdout), process.StandardError.BaseStream.CopyToAsync(stderr));
        try
        {
            while (!process.HasExited)
            {
                ct.ThrowIfCancellationRequested();
                process.Refresh();
                job.ProcessWorkingSetBytes = process.WorkingSet64;
                job.ProcessCpuSeconds = process.TotalProcessorTime.TotalSeconds;
                job.LastObservedAt = DateTimeOffset.UtcNow;
                var progressPath = Directory.GetFiles(Path.GetDirectoryName(request)!, Path.GetFileName(request) + ".progress.*.json")
                    .OrderByDescending(path => path, StringComparer.Ordinal).FirstOrDefault();
                if (progressPath != null)
                {
                    try
                    {
                        await using var progressStream = new FileStream(progressPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        var snapshot = await JsonSerializer.DeserializeAsync<EngineProgress>(progressStream, SimulationWorker.Json, ct);
                        if (snapshot != null) job.Progress?.Report(snapshot);
                    }
                    catch (FileNotFoundException) { /* The worker retained a newer snapshot; observe it next time. */ }
                }
                await Task.Delay(250, ct);
            }
            await process.WaitForExitAsync(ct);
            await copying;
            if (!File.Exists(request + ".result.json"))
                return new ExperimentResult { ExitCode = process.ExitCode, Error = "Worker exited without a result receipt.", OutputDirectory = job.OutputDir };
            return JsonSerializer.Deserialize<ExperimentResult>(await File.ReadAllTextAsync(request + ".result.json", ct), SimulationWorker.Json)
                ?? throw new InvalidDataException("Invalid worker result.");
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await copying;
            throw;
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await copying;
            throw;
        }
        finally { job.ProcessId = null; }
    }
}
