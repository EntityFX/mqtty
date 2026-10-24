using System.Collections.Concurrent;
namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Состояния сохраняются атомарно; прежний Running после рестарта явно Interrupted.</summary>
public sealed class JobRegistry : IJobRegistry
{
    private readonly ConcurrentDictionary<string, SimulationJob> _jobs = new();
    private readonly JsonFileStore _store;
    public JobRegistry(JsonFileStore store)
    {
        _store = store;
        foreach (var id in store.ListFiles("jobs", "*.json"))
        {
            var job = store.ReadAsync<SimulationJob>($"jobs/{id}.json").GetAwaiter().GetResult();
            if (job == null) continue;
            // This host does not claim ownership of an old process or silently restart a scientific run.
            if (job.State is "Running" or "Queued" or "Paused" or "Cancelling")
            { job.State = "Interrupted"; job.Error = "Web host stopped before a terminal receipt; previous output retained."; job.FinishedAt = DateTimeOffset.UtcNow; job.ProcessId = null; }
            Add(job);
        }
    }
    public void Add(SimulationJob job)
    {
        lock (job.SyncRoot)
        { _jobs[job.Id] = job; _store.WriteAsync($"jobs/{job.Id}.json", job).GetAwaiter().GetResult(); }
    }
    public SimulationJob? Get(string id) => _jobs.GetValueOrDefault(id);
    public IReadOnlyList<SimulationJob> GetAll() => _jobs.Values.OrderByDescending(j => j.StartedAt).ToList();
    public void Remove(string id) { if (_jobs.TryRemove(id, out _)) _store.Delete($"jobs/{id}.json"); }
}
