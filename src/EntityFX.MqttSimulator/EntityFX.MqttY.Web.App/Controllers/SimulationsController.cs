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
    private readonly IPresetCatalogService _catalog;
    private readonly ITopologyGeneratorService _generator;
    private readonly IConfigurationMapperService _mapper;
    private readonly ISimulationJobRunner _runner;
    private readonly IJobRegistry _registry;
    private readonly IMonitoringService _monitoring;
    private readonly DataDirectoryOptions _options;
    private readonly NativeConfigurationService _native;
    private readonly JsonFileStore _store;

    public SimulationsController(
        IPresetCatalogService catalog,
        ITopologyGeneratorService generator,
        IConfigurationMapperService mapper,
        ISimulationJobRunner runner,
        IJobRegistry registry,
        IMonitoringService monitoring,
        DataDirectoryOptions options, NativeConfigurationService native, JsonFileStore store)
    {
        _catalog = catalog;
        _generator = generator;
        _mapper = mapper;
        _runner = runner;
        _registry = registry;
        _monitoring = monitoring;
        _options = options;
        _native = native;
        _store = store;
    }

    /// <summary>Создаёт и запускает симуляцию.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SimulationCreateDto request)
    {
        try
        {
        SimulationStorageGuard.Check(_options, Path.GetFullPath(_options.DataDirectory));
        if (request.Engine is not ("relay" or "federation" or "loadBalancing")) return BadRequest(new { error = "Unknown engine." });
        if (request.RunSettings.ExecutionMode is not ("" or "InProcess" or "OutOfProcess")) return BadRequest(new { error = "Unknown execution mode." });

        var spec = await ResolveSpecAsync(request);
        if (spec == null)
        {
            return BadRequest(new { error = "Topology not found or graph is empty." });
        }

        var saved=request.TopologyId==null?null:await _store.ReadAsync<SavedTopologyDto>($"topologies/{request.TopologyId}.json");
        var supplied=request.Graph??saved?.Graph;
        if(supplied!=null) {
            var ids=supplied.Nodes.Select(n=>n.Id).Concat(supplied.Networks.Select(n=>n.Id)).ToArray();
            if(ids.Any(string.IsNullOrWhiteSpace)||ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Идентификаторы узлов и сетей должны быть уникальными и непустыми.");
        }
        var expected=_generator.Generate(spec);
        var edited=supplied!=null && GraphStructure(supplied)!=GraphStructure(_mapper.ToGraphDto(expected));
        var graph=edited?_mapper.ToNetworkGraphOption(supplied!):expected;
        var runOptions = _mapper.ToRunOptions(graph, request.Engine, spec);
        await _native.PopulateAsync(runOptions, spec, edited);
        if(request.ValidateOnly)return Ok(new { valid=true, editedGraph=edited, networks=graph.Networks.Count, brokers=graph.Nodes.Values.Count(n=>n.Type==EntityFX.MqttY.Contracts.Options.NodeOptionType.Server) });

        var job = new SimulationJob
        {
            TopologyId = request.TopologyId,
            Name = saved?.Name,
            Engine = request.Engine,
            RunOptions = runOptions,
            TimeLimit = request.RunSettings.TimeLimit ?? _options.DefaultTimeLimit,
            Parallel = request.RunSettings.Parallel || _options.DefaultParallel,
            ExecutionMode = string.IsNullOrWhiteSpace(request.RunSettings.ExecutionMode)
                ? _options.DefaultExecutionMode
                : request.RunSettings.ExecutionMode
        };

        var id = await _runner.EnqueueAsync(job);
        return Accepted(ToStatusDto(job));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet]
    public IActionResult GetSimulations() =>
        Ok(_registry.GetAll().Select(ToStatusDto));

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
            items = jobs.Skip((page - 1) * pageSize).Take(pageSize).Select(ToStatusDto) });
    }

    [HttpGet("{id}")]
    public IActionResult GetSimulation(string id)
    {
        var job = _registry.Get(id);
        return job == null ? NotFound() : Ok(ToStatusDto(job));
    }

    [HttpPost("{id}/pause")]
    public async Task<IActionResult> Pause(string id)
    {
        try { await _runner.PauseAsync(id); }
        catch (InvalidOperationException error) { return Conflict(new { error = error.Message }); }
        return Ok(ToStatusDto(_registry.Get(id)));
    }

    [HttpPost("{id}/resume")]
    public async Task<IActionResult> Resume(string id)
    {
        try { await _runner.ResumeAsync(id); }
        catch (InvalidOperationException error) { return Conflict(new { error = error.Message }); }
        return Ok(ToStatusDto(_registry.Get(id)));
    }

    [HttpPost("{id}/stop")]
    public async Task<IActionResult> Stop(string id)
    {
        await _runner.StopAsync(id);
        return Ok(ToStatusDto(_registry.Get(id)));
    }

    [HttpPost("{id}/restart")]
    public async Task<IActionResult> Restart(string id)
    {
        var original = _registry.Get(id);
        if (original == null) return NotFound();
        if (original.State is "Running" or "Paused" or "Queued" or "Cancelling")
            return Conflict(new { error = "Сначала завершите или остановите текущий запуск." });
        var copy = new SimulationJob { TopologyId = original.TopologyId, Name = original.Name, Engine = original.Engine,
            RunOptions = original.RunOptions, TimeLimit = original.TimeLimit,
            Parallel = original.Parallel, ExecutionMode = original.ExecutionMode };
        try { await _runner.EnqueueAsync(copy); }
        catch (InvalidOperationException error) { return Conflict(new { error = error.Message }); }
        return Accepted(ToStatusDto(copy));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        var job = _registry.Get(id);
        if (job == null) return NotFound();
        if (job.State is "Running" or "Cancelling" or "Queued" or "Paused")
            return Conflict(new { error = "Stop the job and wait for terminal status before deleting its registry entry. Scientific output is retained." });

        _registry.Remove(id);
        return NoContent();
    }

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
        if (job?.OutputDir == null || !Directory.Exists(job.OutputDir))
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
        if (job?.OutputDir == null || Path.GetFileName(file) != file) return NotFound();
        var path = Path.Combine(job.OutputDir, file);
        return System.IO.File.Exists(path) ? PhysicalFile(path, "application/octet-stream", file) : NotFound();
    }

    /// <summary>Экспорт манифеста симуляции (manifest.json) для сохранения вне приложения.</summary>
    [HttpGet("{id}/manifest")]
    public IActionResult ExportManifest(string id)
    {
        var job = _registry.Get(id);
        if (job?.OutputDir == null || !Directory.Exists(job.OutputDir)) return NotFound();
        var manifestPath = Path.Combine(job.OutputDir, "experiment-manifest.json");
        if (!System.IO.File.Exists(manifestPath)) manifestPath = Path.Combine(job.OutputDir, "manifest.json");
        if (!System.IO.File.Exists(manifestPath)) return NotFound(new { error = "Manifest not found." });
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

    private async Task<TopologySpecDto?> ResolveSpecAsync(SimulationCreateDto request)
    {
        if (request.Spec != null) { request.Spec.Engine = request.Engine; return request.Spec; }
        if (request.Graph != null)
        {
            return new TopologySpecDto { Engine = request.Engine };
        }

        if (string.IsNullOrWhiteSpace(request.TopologyId))
        {
            return null;
        }

        var preset = await _catalog.GetTopologyPresetAsync(request.TopologyId);
        if (preset == null)
        {
            var saved = await _store.ReadAsync<SavedTopologyDto>($"topologies/{request.TopologyId}.json");
            if (saved == null) return null;
            saved.Spec.Engine = request.Engine;
            return saved.Spec;
        }

        return new TopologySpecDto
        {
            Kind = preset.Kind,
            ChainLength = preset.ChainLength,
            Rays = preset.Rays,
            RingLength = preset.RingLength,
            BrokerPresetId = preset.BrokerPresetId,
            ClientsPerBroker = preset.ClientsPerBroker,
            RelayApps = preset.RelayApps,
            Engine = request.Engine,
            NetworkLength = preset.NetworkLength,
            Qos = preset.Qos,
            PayloadBytes = preset.PayloadBytes,
            OfferedRps = preset.OfferedRps,
            WarmupTicks = preset.WarmupTicks,
            MeasurementTicks = preset.MeasurementTicks,
            DrainTimeoutTicks = preset.DrainTimeoutTicks,
            Seed = preset.Seed,
            NetworkType = preset.NetworkType
        };
    }

    private static string GraphStructure(GraphDto graph)
    {
        var normalized = new {
            graph.RandomSeed, graph.Ticks,
            Networks = graph.Networks.OrderBy(n => n.Id).Select(n => new { n.Id, n.NetworkType }),
            Nodes = graph.Nodes.OrderBy(n => n.Id).Select(n => new { n.Id, n.Type, n.Specification, n.Network, n.Broker, n.ConnectsToServer, n.Configuration, n.Additional }),
            Links = graph.Links.OrderBy(l => l.From).ThenBy(l => l.To).Select(l => new { l.From, l.To, l.Weight }),
            graph.NetworkTypes
        };
        return System.Text.Json.JsonSerializer.Serialize(normalized, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    }

    private static SimulationStatusDto ToStatusDto(SimulationJob? job)
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
            TickMilliseconds = job.RunOptions.Graph.Ticks.TickPeriod.TotalMilliseconds,
            Engine = job.Engine,
            State = job.State,
            TotalTicks = job.TotalTicks,
            Step = job.Step,
            Errors = job.Errors,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt,
            OutputDir = job.OutputDir,
            Error = job.Error,
            ProcessId = job.ProcessId
            ,ProcessWorkingSetBytes = job.ProcessWorkingSetBytes, ProcessCpuSeconds = job.ProcessCpuSeconds,
            LastObservedAt = job.LastObservedAt, Phase = job.Phase
        };
    }
}
