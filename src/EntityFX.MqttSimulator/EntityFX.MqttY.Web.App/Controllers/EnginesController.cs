using EntityFX.MqttY.Engines.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace EntityFX.MqttY.Web.App.Controllers;

/// <summary>Список доступных движков.</summary>
[ApiController]
[Route("api/engines")]
public sealed class EnginesController : ControllerBase
{
    private readonly EngineHost _engineHost;

    public EnginesController(EngineHost engineHost)
    {
        _engineHost = engineHost;
    }

    /// <summary>Возвращает список движков: relay, federation, loadBalancing.</summary>
    [HttpGet]
    public IActionResult GetEngines() => Ok(_engineHost.Definitions);
}
