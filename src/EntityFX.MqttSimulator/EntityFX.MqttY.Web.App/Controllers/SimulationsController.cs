using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
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

    public SimulationsController(
        IPresetCatalogService catalog,
        ITopologyGeneratorService generator,
        IConfigurationMapperService mapper,
        ISimulationJobRunner runner,
        IJobRegistry registry,
        IMonitoringService monitoring,
        DataDirectoryOptions options, NativeConfigurationService native)
    {
        _catalog = catalog;
        _generator = generator;
        _mapper = mapper;
        _runner = runner;
        _registry = registry;
        _monitoring = monitoring;
        _options = options;
        _native = native;
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
        if (request.Graph != null) return BadRequest(new { error = "Edited graph execution is not supported by native adapters yet. Submit spec or a topology preset; graph editing remains available for design." });
        var spec = await ResolveSpecAsync(request);
        if (spec == null)
        {
            return BadRequest(new { error = "Topology not found or graph is empty." });
        }

        var graph = request.Graph != null
            ? _mapper.ToNetworkGraphOption(request.Graph)
            : _generator.Generate(spec);

        var runOptions = _mapper.ToRunOptions(graph, request.Engine, spec);
        await _native.PopulateAsync(runOptions, spec);

        var job = new SimulationJob
        {
            TopologyId = request.TopologyId,
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
        catch (Exception ex) when (ex is ArgumentException or IOException or KeyNotFoundException or InvalidOperationException)
        { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet]
    public IActionResult GetSimulations() =>
        Ok(_registry.GetAll().Select(ToStatusDto));

    [HttpGet("{id}")]
    public IActionResult GetSimulation(string id)
    {
        var job = _registry.Get(id);
        return job == null ? NotFound() : Ok(ToStatusDto(job));
    }

    [HttpPost("{id}/pause")]
    public async Task<IActionResult> Pause(string id)
    {
        await _runner.PauseAsync(id);
        return Ok(ToStatusDto(_registry.Get(id)));
    }

    [HttpPost("{id}/resume")]
    public async Task<IActionResult> Resume(string id)
    {
        await _runner.ResumeAsync(id);
        return Ok(ToStatusDto(_registry.Get(id)));
    }

    [HttpPost("{id}/stop")]
    public async Task<IActionResult> Stop(string id)
    {
        await _runner.StopAsync(id);
        return Ok(ToStatusDto(_registry.Get(id)));
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
            return null;
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
