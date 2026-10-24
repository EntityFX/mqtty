using System.Text.Json;
using EntityFX.MqttY.Engines.Execution;
namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Изолированный процесс использует те же native runners, что и адаптеры библиотеки.</summary>
public static class SimulationWorker
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static async Task<int> ExecuteAsync(string requestPath)
    {
        var job = JsonSerializer.Deserialize<SimulationJob>(await File.ReadAllTextAsync(requestPath), Json)
            ?? throw new InvalidDataException("Empty worker request.");
        try
        {
            var progress = new SynchronousProgress(snapshot =>
            {
                var path = requestPath + ".progress." + snapshot.TotalTicks.ToString("D20") + ".json";
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(snapshot, Json));
                File.Move(path + ".tmp", path);
                foreach (var stale in Directory.GetFiles(Path.GetDirectoryName(requestPath)!, Path.GetFileName(requestPath) + ".progress.*.json")
                    .OrderByDescending(p => p, StringComparer.Ordinal).Skip(2))
                    try { File.Delete(stale); } catch (IOException) { }
            });
            var result = NativeExperimentAdapter.Run(job.RunOptions, job.OutputDir, progress: progress);
            await File.WriteAllTextAsync(requestPath + ".result.json", JsonSerializer.Serialize(result, Json));
            return result.ExitCode;
        }
        catch (Exception exception)
        {
            var result = new EntityFX.MqttY.Engines.Contracts.ExperimentResult { ExitCode = 1, Error = exception.ToString(), OutputDirectory = job.OutputDir };
            await File.WriteAllTextAsync(requestPath + ".result.json", JsonSerializer.Serialize(result, Json));
            return 1;
        }
    }
}
