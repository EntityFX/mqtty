using System.Collections.Concurrent;
using EntityFX.MqttY.Engines.Contracts;
using EntityFX.MqttY.Web.App.Models;

namespace EntityFX.MqttY.Web.App.Services;

/// <summary>Реализация мониторинга: хранит последний снимок и кольцевой буфер лога на задание.</summary>
public sealed class MonitoringService : IMonitoringService
{
    private const int MaxLogItems = 1000;

    private readonly ConcurrentDictionary<string, MonitoringSnapshotDto> _snapshots = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<LogItemDto>> _logs = new();
    private readonly JsonFileStore? _store;

    public MonitoringService(JsonFileStore? store = null)
    {
        _store = store;
        if (store == null) return;
        foreach (var id in store.ListFiles("monitoring", "*.json"))
        {
            var snapshot = store.ReadAsync<MonitoringSnapshotDto>($"monitoring/{id}.json").GetAwaiter().GetResult();
            if (snapshot != null) _snapshots[id] = snapshot;
        }
    }

    public event Action<string, MonitoringSnapshotDto>? MonitoringUpdated;

    public event Action<string, NodeStatusDto>? NodeStatusUpdated;

    public event Action<string, SimulationStatusDto>? StateChanged;

    public void Update(string simulationId, EngineProgress progress)
    {
        var snapshot = new MonitoringSnapshotDto
        {
            SimulationId = simulationId,
            State = "Running",
            TotalTicks = progress.TotalTicks,
            Step = progress.Step,
            Errors = progress.Errors,
            Phase = progress.Phase,
            Links = progress.Links?.Select(link => new LinkDto { From = link.From, To = link.To }).ToArray() ?? Array.Empty<LinkDto>(),
            Nodes = progress.Nodes?
                .Select(n => new NodeStatusDto
                {
                    Id = n.Id,
                    Name = n.Name,
                    Type = n.Type,
                    Specification = n.Specification,
                    Network = n.Network,
                    Connected = n.Connected,
                    QueueLength = n.QueueLength,
                    Counters = n.Counters
                })
                .ToList() ?? new List<NodeStatusDto>()
        };

        _snapshots[simulationId] = snapshot;
        Persist(simulationId, snapshot);
        MonitoringUpdated?.Invoke(simulationId, snapshot);

        if (progress.Nodes != null)
        {
            foreach (var node in progress.Nodes)
            {
                NodeStatusUpdated?.Invoke(simulationId, new NodeStatusDto
                {
                    Id = node.Id,
                    Name = node.Name,
                    Type = node.Type,
                    Specification = node.Specification,
                    Network = node.Network,
                    Connected = node.Connected,
                    QueueLength = node.QueueLength,
                    Counters = node.Counters
                });
            }
        }
    }

    public void SetState(string simulationId, string state)
    {
        var snapshot = _snapshots.GetOrAdd(simulationId, _ => new MonitoringSnapshotDto { SimulationId = simulationId });
        snapshot.State = state;
        Persist(simulationId, snapshot);

        StateChanged?.Invoke(simulationId, new SimulationStatusDto { Id = simulationId, State = state });
    }

    public MonitoringSnapshotDto? GetSnapshot(string simulationId) =>
        _snapshots.TryGetValue(simulationId, out var snapshot) ? snapshot : null;

    public NodeStatusDto? GetNode(string simulationId, string nodeId)
    {
        var snapshot = GetSnapshot(simulationId);
        return snapshot?.Nodes.FirstOrDefault(n => n.Id == nodeId);
    }

    public IReadOnlyList<LogItemDto> GetLog(string simulationId, int offset)
    {
        if (!_logs.TryGetValue(simulationId, out var queue))
        {
            return Array.Empty<LogItemDto>();
        }

        return queue.Skip(offset).ToList();
    }

    public void AppendLog(string simulationId, LogItemDto item)
    {
        var queue = _logs.GetOrAdd(simulationId, _ => new ConcurrentQueue<LogItemDto>());
        queue.Enqueue(item);
        while (queue.Count > MaxLogItems && queue.TryDequeue(out _))
        {
        }
    }

    private void Persist(string id, MonitoringSnapshotDto snapshot) =>
        _store?.WriteAsync($"monitoring/{id}.json", snapshot).GetAwaiter().GetResult();
}
