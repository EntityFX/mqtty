using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;

namespace EntityFX.MqttY.Web.App.Application;

/// <summary>Save/clone/update/delete operations for persisted topologies.</summary>
public sealed class SaveTopologyHandler
{
    private const string TopologiesDir = "topologies";
    private readonly JsonFileStore _store;

    public SaveTopologyHandler(JsonFileStore store) => _store = store;

    public async Task<ApplicationResult<SavedTopologyDto>> SaveAsync(SavedTopologyDto topology)
    {
        if (string.IsNullOrWhiteSpace(topology.Id))
        {
            topology.Id = Guid.NewGuid().ToString("N");
        }

        await _store.WriteAsync($"{TopologiesDir}/{topology.Id}.json", topology);
        return ApplicationResult<SavedTopologyDto>.Ok(topology);
    }

    public async Task<ApplicationResult<SavedTopologyDto>> UpdateAsync(string id, SavedTopologyDto topology)
    {
        topology.Id = id;
        await _store.WriteAsync($"{TopologiesDir}/{id}.json", topology);
        return ApplicationResult<SavedTopologyDto>.Ok(topology);
    }

    public async Task<ApplicationResult<SavedTopologyDto>> CloneAsync(string id, CloneTopologyRequest? request)
    {
        var saved = await _store.ReadAsync<SavedTopologyDto>($"{TopologiesDir}/{id}.json");
        if (saved == null) return ApplicationResult<SavedTopologyDto>.NotFound();

        var clone = new SavedTopologyDto
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = string.IsNullOrWhiteSpace(request?.Name) ? $"Копия {saved.Name}" : request.Name,
            Graph = saved.Graph,
            Spec = saved.Spec
        };

        await _store.WriteAsync($"{TopologiesDir}/{clone.Id}.json", clone);
        return ApplicationResult<SavedTopologyDto>.Ok(clone);
    }

    public ApplicationResult<bool> Delete(string id)
    {
        _store.Delete($"{TopologiesDir}/{id}.json");
        return ApplicationResult<bool>.NoContent();
    }

    public ApplicationResult<bool> ClearAll()
    {
        foreach (var file in _store.ListFiles(TopologiesDir, "*.json"))
        {
            _store.Delete($"{TopologiesDir}/{file}.json");
        }

        return ApplicationResult<bool>.NoContent();
    }
}
