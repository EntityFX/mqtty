using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace EntityFX.MqttY.Web.App.Controllers;

/// <summary>Пресеты брокеров и топологий.</summary>
[ApiController]
[Route("api/presets")]
public sealed class PresetsController : ControllerBase
{
    private readonly IPresetCatalogService _catalog;

    public PresetsController(IPresetCatalogService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("brokers")]
    public async Task<IActionResult> GetBrokers() => Ok(await _catalog.GetBrokerPresetsAsync());

    [HttpGet("brokers/{id}")]
    public async Task<IActionResult> GetBroker(string id)
    {
        var preset = await _catalog.GetBrokerPresetAsync(id);
        return preset == null ? NotFound() : Ok(preset);
    }

    [HttpPost("brokers")]
    public async Task<IActionResult> SaveBroker([FromBody] BrokerPresetDto preset) =>
        Ok(await _catalog.SaveBrokerPresetAsync(preset));

    [HttpPut("brokers/{id}")]
    public async Task<IActionResult> UpdateBroker(string id, [FromBody] BrokerPresetDto preset)
    {
        preset.Id = id;
        return Ok(await _catalog.SaveBrokerPresetAsync(preset));
    }

    [HttpDelete("brokers/{id}")]
    public async Task<IActionResult> DeleteBroker(string id)
    {
        await _catalog.DeleteBrokerPresetAsync(id);
        return NoContent();
    }

    [HttpGet("topologies")]
    public async Task<IActionResult> GetTopologies() => Ok(await _catalog.GetTopologyPresetsAsync());

    [HttpGet("topologies/{id}")]
    public async Task<IActionResult> GetTopology(string id)
    {
        var preset = await _catalog.GetTopologyPresetAsync(id);
        return preset == null ? NotFound() : Ok(preset);
    }

    [HttpPost("topologies")]
    public async Task<IActionResult> SaveTopology([FromBody] TopologyPresetDto preset) =>
        Ok(await _catalog.SaveTopologyPresetAsync(preset));

    [HttpPut("topologies/{id}")]
    public async Task<IActionResult> UpdateTopology(string id, [FromBody] TopologyPresetDto preset)
    {
        preset.Id = id;
        return Ok(await _catalog.SaveTopologyPresetAsync(preset));
    }

    [HttpDelete("topologies/{id}")]
    public async Task<IActionResult> DeleteTopology(string id)
    {
        await _catalog.DeleteTopologyPresetAsync(id);
        return NoContent();
    }
}
