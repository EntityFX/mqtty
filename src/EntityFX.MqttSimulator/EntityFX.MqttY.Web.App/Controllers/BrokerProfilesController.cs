using System.Text.Json;
using EntityFX.MqttY.Plugin.Mqtt.BrokerProfile;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace EntityFX.MqttY.Web.App.Controllers;

[ApiController]
[Route("api/presets/brokers/{id}/profile")]
public sealed class BrokerProfilesController : ControllerBase
{
    private readonly IPresetCatalogService _catalog;
    private readonly JsonFileStore _store;
    public BrokerProfilesController(IPresetCatalogService catalog, JsonFileStore store) { _catalog = catalog; _store = store; }

    [HttpGet]
    public async Task<IActionResult> Get(string id)
    {
        var preset = await _catalog.GetBrokerPresetAsync(id);
        if (preset == null || string.IsNullOrWhiteSpace(preset.ProfilePath)) return NotFound(new { error = "У пресета нет измеренного профиля." });
        try {
            using var document = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(_store.Resolve(preset.ProfilePath)));
            return Ok(document.RootElement.Clone());
        } catch (Exception error) when (error is IOException or ArgumentException or JsonException or InvalidOperationException) { return BadRequest(new { error = error.Message }); }
    }

    [HttpPut]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> SaveCopy(string id, [FromBody] JsonElement profile)
    {
        var preset = await _catalog.GetBrokerPresetAsync(id);
        if (preset == null) return NotFound();
        try {
            // Validate exact broker name and schema before linking a new immutable copy.
            new BrokerBenchmarkRepository(profile.GetRawText()).Get(preset.BrokerType);
            var relative = $"profiles/custom/{Guid.NewGuid():N}.json";
            await _store.WriteAsync(relative, profile);
            preset.ProfilePath = relative;
            return Ok(await _catalog.SaveBrokerPresetAsync(preset));
        } catch (Exception error) when (error is ArgumentException or InvalidDataException or KeyNotFoundException or JsonException) { return BadRequest(new { error = error.Message }); }
    }
}
