using EntityFX.MqttY.Web.App.Application;
using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EntityFX.MqttY.Web.App.Controllers;

/// <summary>Создание, управление и мониторинг симуляций.</summary>
[ApiController]
[Route("api/simulations")]
public sealed class SimulationsController : ControllerBase
{
    private readonly SimulationCreationHandler _creation;
    private readonly SimulationJobHandler _jobs;
    private readonly IJobRegistry _registry;
    private readonly IMonitoringService _monitoring;
    private readonly JsonFileStore _store;

    public SimulationsController(
        SimulationCreationHandler creation,
        SimulationJobHandler jobs,
        IJobRegistry registry,
        IMonitoringService monitoring,
        JsonFileStore store)
    {
        _creation = creation;
        _jobs = jobs;
        _registry = registry;
        _monitoring = monitoring;
        _store = store;
    }

    /// <summary>Создаёт и запускает симуляцию.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SimulationCreateDto request)
    {
        var result = await _creation.HandleAsync(request);
        return result.Status switch
        {
            ApplicationStatus.Ok => Ok(result.Value),
            ApplicationStatus.Accepted => Accepted(result.Value),
            ApplicationStatus.NotFound => NotFound(),
            _ => BadRequest(new { error = result.Error })
        };
    }

    [HttpGet]
    public IActionResult GetSimulations() =>
        Ok(_registry.GetAll().Select(SimulationDtoMapper.ToStatusDto));

    [HttpGet("search")]
    public IActionResult Search([FromQuery] string? q, [FromQuery] string? state,
        [FromQuery] string? engine, [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var jobs = _registry.GetAll().Where(j =>
            (string.IsNullOrWhiteSpace(q) || (j.Id + " " + j.Name + " " + j.TopologyId).Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(state) || j.State == state) &&
            (string.IsNullOrEmpty(engine) || j.Engine == engine) &&
            (!from.HasValue || j.StartedAt >= from) && (!to.HasValue || j.StartedAt <= to))
            .OrderByDescending(j => j.StartedAt).ThenBy(j => j.Id).ToArray();
        var pages = Math.Max(1, (jobs.Length + pageSize - 1) / pageSize);
        page = Math.Min(page, pages);
        return Ok(new { total = jobs.Length, page, pageSize, pages,
            items = jobs.Skip((page - 1) * pageSize).Take(pageSize).Select(SimulationDtoMapper.ToStatusDto) });
    }

    [HttpGet("{id}")]
    public IActionResult GetSimulation(string id)
    {
        var job = _registry.Get(id);
        return job == null ? NotFound() : Ok(SimulationDtoMapper.ToStatusDto(job));
    }

    [HttpPost("{id}/pause")]
    public async Task<IActionResult> Pause(string id) =>
        Map(await _jobs.PauseAsync(id));

    [HttpPost("{id}/resume")]
    public async Task<IActionResult> Resume(string id) =>
        Map(await _jobs.ResumeAsync(id));

    [HttpPost("{id}/stop")]
    public async Task<IActionResult> Stop(string id) =>
        Map(await _jobs.StopAsync(id));

    [HttpPost("{id}/restart")]
    public async Task<IActionResult> Restart(string id) =>
        MapJob(await _jobs.RestartAsync(id));

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        var result = _jobs.Delete(id);
        return result.Status switch
        {
            ApplicationStatus.NoContent => NoContent(),
            ApplicationStatus.NotFound => NotFound(),
            ApplicationStatus.Conflict => Conflict(new { error = result.Error }),
            _ => BadRequest(new { error = result.Error })
        };
    }

    private IActionResult Map(ApplicationResult<SimulationStatusDto> result) =>
        result.Status switch
        {
            ApplicationStatus.Ok => Ok(result.Value),
            ApplicationStatus.Conflict => Conflict(new { error = result.Error }),
            ApplicationStatus.NotFound => NotFound(),
            _ => BadRequest(new { error = result.Error })
        };

    private IActionResult MapJob(ApplicationResult<SimulationStatusDto> result) =>
        result.Status switch
        {
            ApplicationStatus.Accepted => Accepted(result.Value),
            ApplicationStatus.Conflict => Conflict(new { error = result.Error }),
            ApplicationStatus.NotFound => NotFound(),
            _ => BadRequest(new { error = result.Error })
        };

    [HttpGet("{id}/monitor")]
    public IActionResult Monitor(string id)
    {
        var snapshot = _monitoring.GetSnapshot(id);
        if (snapshot != null && _registry.Get(id) is { } job) snapshot.State = job.State;
        return snapshot == null ? NotFound() : Ok(snapshot);
    }

    [HttpGet("{id}/nodes/{nodeId}")]
    public IActionResult GetNode(string id, string nodeId)
    {
        var node = _monitoring.GetNode(id, nodeId);
        return node == null ? NotFound() : Ok(node);
    }

    [HttpGet("{id}/log")]
    public IActionResult GetLog(string id, [FromQuery] int offset = 0) =>
        Ok(_monitoring.GetLog(id, offset));

    [HttpGet("{id}/results")]
    public IActionResult GetResults(string id)
    {
        var job = _registry.Get(id);
        if (job?.OutputDir == null || !_store.IsWithinRoot(job.OutputDir) || !Directory.Exists(job.OutputDir))
        {
            return NotFound();
        }

        var files = Directory.GetFiles(job.OutputDir)
            .Select(path => Path.GetFileName(path)!)
            .ToDictionary(name => name, name => $"/api/simulations/{id}/results/{Uri.EscapeDataString(name)}");
        return Ok(files);
    }

    [HttpGet("{id}/results/{file}")]
    public IActionResult Download(string id, string file)
    {
        var job = _registry.Get(id);
        if (job?.OutputDir == null || Path.GetFileName(file) != file || !_store.IsWithinRoot(job.OutputDir)) return NotFound();
        var path = Path.Combine(job.OutputDir, file);
        if (!_store.IsWithinRoot(path)) return NotFound();
        return System.IO.File.Exists(path) ? PhysicalFile(path, "application/octet-stream", file) : NotFound();
    }

    /// <summary>Экспорт манифеста симуляции (manifest.json) для сохранения вне приложения.</summary>
    [HttpGet("{id}/manifest")]
    public IActionResult ExportManifest(string id)
    {
        var job = _registry.Get(id);
        if (job?.OutputDir == null || !_store.IsWithinRoot(job.OutputDir) || !Directory.Exists(job.OutputDir)) return NotFound();
        var manifestPath = Path.Combine(job.OutputDir, "experiment-manifest.json");
        if (!System.IO.File.Exists(manifestPath)) manifestPath = Path.Combine(job.OutputDir, "manifest.json");
        if (!System.IO.File.Exists(manifestPath) || !_store.IsWithinRoot(manifestPath)) return NotFound(new { error = "Manifest not found." });
        return PhysicalFile(manifestPath, "application/json", $"manifest-{id}.json");
    }

    /// <summary>Импорт топологии из загруженного JSON-файла (манифеста или графа).</summary>
    [HttpPost("import")]
    public async Task<IActionResult> ImportTopology(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "File is required." });
        using var reader = new StreamReader(file.OpenReadStream());
        var content = await reader.ReadToEndAsync();
        try
        {
            var imported = System.Text.Json.JsonSerializer.Deserialize<SavedTopologyDto>(content, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (imported == null) return BadRequest(new { error = "Invalid topology JSON." });
            if (string.IsNullOrWhiteSpace(imported.Id)) imported.Id = Guid.NewGuid().ToString("N");
            imported.Name = string.IsNullOrWhiteSpace(imported.Name) ? $"Импортировано {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}" : imported.Name;
            await _store.WriteAsync($"topologies/{imported.Id}.json", imported);
            return Ok(imported);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Не удалось импортировать топологию: {ex.Message}" });
        }
    }

}
