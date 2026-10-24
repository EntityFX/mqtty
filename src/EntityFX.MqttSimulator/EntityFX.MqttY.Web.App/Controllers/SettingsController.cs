using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EntityFX.MqttY.Web.App.Controllers;

/// <summary>Настройки приложения: лимит времени, параллельность, режим исполнения, профили брокеров.</summary>
[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly DataDirectoryOptions _options;
    private readonly JsonFileStore _store;

    public SettingsController(DataDirectoryOptions options, JsonFileStore store)
    {
        _options = options;
        _store = store;
    }

    [HttpGet]
    public IActionResult GetSettings() => Ok(ToDto());

    [HttpPut]
    public IActionResult UpdateSettings([FromBody] AppSettingsDto settings)
    {
        if (settings.TimeLimit <= TimeSpan.Zero || settings.TimeLimit > TimeSpan.FromHours(24)) return BadRequest(new { error = "TimeLimit must be positive and at most 24 hours." });
        if (settings.ExecutionMode is not ("InProcess" or "OutOfProcess")) return BadRequest(new { error = "Unknown execution mode." });
        if (settings.MaxParallelJobs < 1 || settings.MaxParallelJobs > _options.MaxParallelJobLimit) return BadRequest(new { error = $"MaxParallelJobs must be between 1 and configured limit {_options.MaxParallelJobLimit}." });
        _options.DefaultTimeLimit = settings.TimeLimit;
        _options.DefaultParallel = settings.Parallel;
        _options.DefaultExecutionMode = settings.ExecutionMode;
        _options.MaxParallelJobs = Math.Max(1, settings.MaxParallelJobs);
        return Ok(ToDto());
    }

    /// <summary>Чтение содержимого профиля брокера по относительному пути профиля.</summary>
    [HttpGet("broker-profile")]
    public IActionResult GetBrokerProfile([FromQuery] string profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath)) return BadRequest(new { error = "profilePath is required." });
        try
        {
            var fullPath = _store.Resolve(profilePath);
            if (!System.IO.File.Exists(fullPath)) return NotFound(new { error = "Profile file not found." });
            var content = System.IO.File.ReadAllText(fullPath);
            return Ok(new { profilePath, content });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Сохранение содержимого профиля брокера.</summary>
    [HttpPut("broker-profile")]
    public IActionResult UpdateBrokerProfile([FromBody] BrokerProfileUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ProfilePath)) return BadRequest(new { error = "profilePath is required." });
        if (dto.Content == null) return BadRequest(new { error = "content is required." });
        try
        {
            // Валидируем, что content — валидный JSON
            JsonDocument.Parse(dto.Content);
            var fullPath = _store.Resolve(dto.ProfilePath);
            System.IO.File.WriteAllText(fullPath, dto.Content);
            return Ok(new { profilePath = dto.ProfilePath });
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private AppSettingsDto ToDto() => new()
    {
        TimeLimit = _options.DefaultTimeLimit,
        Parallel = _options.DefaultParallel,
        ExecutionMode = _options.DefaultExecutionMode,
        MaxParallelJobs = _options.MaxParallelJobs,
        DataDirectory = _options.DataDirectory
    };
}
