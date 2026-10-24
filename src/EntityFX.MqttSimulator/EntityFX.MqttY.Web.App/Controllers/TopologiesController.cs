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

    public TopologiesController(
        ITopologyGeneratorService generator,
        IConfigurationMapperService mapper,
        JsonFileStore store)
    {
        _generator = generator;
        _mapper = mapper;
        _store = store;
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
        if (string.IsNullOrWhiteSpace(topology.Id))
        {
            topology.Id = Guid.NewGuid().ToString("N");
        }

        await _store.WriteAsync($"{TopologiesDir}/{topology.Id}.json", topology);
        return Ok(topology);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTopology(string id, [FromBody] SavedTopologyDto topology)
    {
        topology.Id = id;
        await _store.WriteAsync($"{TopologiesDir}/{id}.json", topology);
        return Ok(topology);
    }

    [HttpDelete("{id}")]
    public IActionResult DeleteTopology(string id)
    {
        _store.Delete($"{TopologiesDir}/{id}.json");
        return NoContent();
    }
}
