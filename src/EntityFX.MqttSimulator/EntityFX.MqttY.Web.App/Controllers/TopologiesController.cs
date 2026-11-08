using EntityFX.MqttY.Web.App.Application;
using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace EntityFX.MqttY.Web.App.Controllers;

/// <summary>Генерация и сохранение топологий.</summary>
[ApiController]
[Route("api/topologies")]
public sealed class TopologiesController : ControllerBase
{
    private const string TopologiesDir = "topologies";

    private readonly ITopologyGeneratorService _generator;
    private readonly IConfigurationMapperService _mapper;
    private readonly JsonFileStore _store;
    private readonly SaveTopologyHandler _save;

    public TopologiesController(
        ITopologyGeneratorService generator,
        IConfigurationMapperService mapper,
        JsonFileStore store,
        SaveTopologyHandler save)
    {
        _generator = generator;
        _mapper = mapper;
        _store = store;
        _save = save;
    }

    /// <summary>Клонирует существующую топологию с новым именем.</summary>
    [HttpPost("{id}/clone")]
    public async Task<IActionResult> CloneTopology(string id, [FromBody] CloneTopologyRequest? request = null)
    {
        var result = await _save.CloneAsync(id, request);
        return result.Status == ApplicationStatus.Ok ? Ok(result.Value) : NotFound();
    }

    /// <summary>Очищает все сохранённые топологии (в ноль).</summary>
    [HttpDelete]
    public IActionResult ClearAllTopologies()
    {
        _save.ClearAll();
        return NoContent();
    }

    /// <summary>Генерирует топологию по параметрам: граф для редактора + execution-конфиг.</summary>
    [HttpPost("generate")]
    public IActionResult Generate([FromBody] TopologySpecDto spec)
    {
        try
        {
            var graph = _generator.Generate(spec);
            var graphDto = _mapper.ToGraphDto(graph);
            var runOptions = _mapper.ToRunOptions(graph, spec.Engine, spec);
            return Ok(new { graph = graphDto, runOptions });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTopologies()
    {
        var result = new List<SavedTopologyDto>();
        foreach (var id in _store.ListFiles(TopologiesDir, "*.json"))
        {
            var saved = await _store.ReadAsync<SavedTopologyDto>($"{TopologiesDir}/{id}.json");
            if (saved != null)
            {
                result.Add(saved);
            }
        }

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetTopology(string id)
    {
        var saved = await _store.ReadAsync<SavedTopologyDto>($"{TopologiesDir}/{id}.json");
        return saved == null ? NotFound() : Ok(saved);
    }

    [HttpPost]
    public async Task<IActionResult> SaveTopology([FromBody] SavedTopologyDto topology)
    {
        var result = await _save.SaveAsync(topology);
        return result.Status == ApplicationStatus.Ok ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTopology(string id, [FromBody] SavedTopologyDto topology)
    {
        var result = await _save.UpdateAsync(id, topology);
        return result.Status == ApplicationStatus.Ok ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteTopology(string id)
    {
        _save.Delete(id);
        return NoContent();
    }
}
