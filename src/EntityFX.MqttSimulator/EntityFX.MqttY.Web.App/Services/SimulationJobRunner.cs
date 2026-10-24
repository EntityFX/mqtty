using System.Threading.Channels;
using EntityFX.MqttY.Engines.Contracts;
using Microsoft.Extensions.Hosting;
namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Ограниченная очередь; Parallel=false исполняется исключительно, Parallel=true — независимые задания.</summary>
public sealed class SimulationJobRunner : BackgroundService, ISimulationJobRunner
{
    private readonly IJobRegistry _registry;
    private readonly IMonitoringService _monitoring;
    private readonly DataDirectoryOptions _options;
    private readonly IServiceProvider _services;
    private readonly JsonFileStore _store;
    private readonly Channel<SimulationJob> _queue = Channel.CreateBounded<SimulationJob>(new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    public SimulationJobRunner(IJobRegistry registry, IMonitoringService monitoring, DataDirectoryOptions options, IServiceProvider services, JsonFileStore store)
    { _registry = registry; _monitoring = monitoring; _options = options; _services = services; _store = store; }
    public Task<string> EnqueueAsync(SimulationJob job)
    {
        SimulationStorageGuard.Check(_options, _store.Root);
        if (job.TimeLimit is { } limit && (limit <= TimeSpan.Zero || limit > TimeSpan.FromHours(24))) throw new ArgumentException("Time limit must be positive and at most 24 hours.");
        job.OutputDir = _store.Resolve($"simulations/{job.Id}");
        job.State = "Queued";
        _registry.Add(job);
        if (!_queue.Writer.TryWrite(job))
        {
            _registry.Remove(job.Id);
            throw new InvalidOperationException("Simulation queue is full.");
        }
        _monitoring.SetState(job.Id, job.State);
        return Task.FromResult(job.Id);
    }
    public Task StopAsync(string id)
    {
        var job = _registry.Get(id);
        if (job == null) return Task.CompletedTask;
        lock (job.SyncRoot)
        {
            if (job.State is "Completed" or "Failed" or "Cancelled" or "TimedOut" or "Interrupted") return Task.CompletedTask;
            job.Cts?.Cancel();
            if (job.Cts == null) { job.State = "Cancelled"; job.FinishedAt = DateTimeOffset.UtcNow; }
            else job.State = "Cancelling";
            Save(job);
        }
        return Task.CompletedTask;
    }
    public Task PauseAsync(string id)
    {
        var job = _registry.Get(id);
        if (job == null) return Task.CompletedTask;
        lock (job.SyncRoot)
        {
            if (job.State is "Completed" or "Failed" or "Cancelled" or "TimedOut" or "Interrupted" or "Paused") return Task.CompletedTask;
            if (job.State == "Running")
            {
                if (job.ExecutionMode != "OutOfProcess")
                    throw new InvalidOperationException("Пауза выполняющейся модели доступна в отдельном процессе. Используйте OutOfProcess.");
                WorkerProcessControl.SetPaused(job, true);
                job.WorkerPaused = true;
                job.BudgetClock.Stop();
                job.Cts!.CancelAfter(Timeout.InfiniteTimeSpan);
                job.State = "Paused";
                Save(job);
                return Task.CompletedTask;
            }
            if (job.State == "Queued") { job.State = "Paused"; Save(job); }
        }
        return Task.CompletedTask;
    }
    public Task ResumeAsync(string id)
    {
        var job = _registry.Get(id);
        if (job == null) return Task.CompletedTask;
        lock (job.SyncRoot)
        {
            if (job.State == "Paused")
            {
                if (job.WorkerPaused)
                {
                    WorkerProcessControl.SetPaused(job, false);
                    job.WorkerPaused = false;
                    job.BudgetClock.Start();
                    if (job.TimeLimit is { } limit)
                        job.Cts!.CancelAfter(limit > job.BudgetClock.Elapsed ? limit-job.BudgetClock.Elapsed : TimeSpan.FromMilliseconds(1));
                    job.State = "Running"; Save(job);
                    return Task.CompletedTask;
                }
                // An entry may still be queued. A generation stamp prevents duplicate execution.
                job.State = "Queued"; Save(job);
                if (!_queue.Writer.TryWrite(job)) throw new InvalidOperationException("Simulation queue is full.");
            }
        }
        return Task.CompletedTask;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var active = new List<Task>();
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                active.RemoveAll(t => t.IsCompleted);
                if (!job.Parallel) { await Task.WhenAll(active); active.Clear(); await RunJobAsync(job, stoppingToken); }
                else
                {
                    while (active.Count >= Math.Clamp(_options.MaxParallelJobs, 1, 8))
                    { await Task.WhenAny(active); active.RemoveAll(t => t.IsCompleted); }
                    active.Add(Task.Run(() => RunJobAsync(job, stoppingToken), stoppingToken));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { await Task.WhenAll(active); }
    }
    private async Task RunJobAsync(SimulationJob job, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (job.SyncRoot)
        {
            // Cancelled/paused/completed entries and duplicate resume entries must never start.
            if (job.State != "Queued") return;
            job.State = "Running"; job.StartedAt = DateTimeOffset.UtcNow; job.Cts = cts; Save(job);
        }
        job.BudgetClock.Restart();
        if (job.TimeLimit.HasValue) cts.CancelAfter(job.TimeLimit.Value);
        job.Progress = new SynchronousProgress(p =>
        {
            job.TotalTicks = p.TotalTicks; job.Step = p.Step; job.Errors = p.Errors; job.Phase = p.Phase;
            _monitoring.Update(job.Id, p);
        });
        try
        {
            SimulationStorageGuard.Check(_options, _store.Root);
            var executor = job.ExecutionMode.Equals("OutOfProcess", StringComparison.OrdinalIgnoreCase)
                ? (IJobExecutor)_services.GetRequiredService<OutOfProcessJobExecutor>() : _services.GetRequiredService<InProcessJobExecutor>();
            var execution = Task.Run(() => executor.ExecuteAsync(job, cts.Token));
            while (await Task.WhenAny(execution, Task.Delay(1000, stoppingToken)) != execution) Save(job);
            var result = await execution;
            job.TotalTicks = result.TotalTicks; job.Step = result.TotalSteps; job.Errors = result.Errors; job.Error = result.Error;
            job.State = result.Success ? "Completed" : "Failed";
        }
        catch (OperationCanceledException)
        {
            job.State = !stoppingToken.IsCancellationRequested && job.State != "Cancelling" ? "TimedOut" : "Cancelled";
            job.Error = job.State == "TimedOut" ? "Wall-clock time limit exceeded; partial output retained." : "Cancelled; partial output retained.";
        }
        catch (Exception ex) { job.State = "Failed"; job.Error = ex.ToString(); }
        finally
        {
            lock (job.SyncRoot)
            { job.Cts = null; job.WorkerPaused = false; job.BudgetClock.Stop(); job.FinishedAt = DateTimeOffset.UtcNow; Save(job); }
        }
    }
    private void Save(SimulationJob job) { _registry.Add(job); _monitoring.SetState(job.Id, job.State); }
}
