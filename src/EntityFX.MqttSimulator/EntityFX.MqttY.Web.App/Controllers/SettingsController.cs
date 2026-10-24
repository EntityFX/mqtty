using EntityFX.MqttY.Web.App.Models;
using EntityFX.MqttY.Web.App.Services;
using Microsoft.AspNetCore.Mvc;

namespace EntityFX.MqttY.Web.App.Controllers;

/// <summary>Настройки приложения: лимит времени, параллельность, режим исполнения.</summary>
[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly DataDirectoryOptions _options;

    public SettingsController(DataDirectoryOptions options)
    {
        _options = options;
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

    private AppSettingsDto ToDto() => new()
    {
        TimeLimit = _options.DefaultTimeLimit,
        Parallel = _options.DefaultParallel,
        ExecutionMode = _options.DefaultExecutionMode,
        MaxParallelJobs = _options.MaxParallelJobs,
        DataDirectory = _options.DataDirectory
    };
}
