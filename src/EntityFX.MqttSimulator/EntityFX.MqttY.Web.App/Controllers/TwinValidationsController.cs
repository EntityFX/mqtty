using EntityFX.MqttY.Web.App.Application;
using EntityFX.MqttY.Web.App.Models;
using Microsoft.AspNetCore.Mvc;

namespace EntityFX.MqttY.Web.App.Controllers;

[ApiController]
[Route("api/twin-validations")]
public sealed class TwinValidationsController : ControllerBase
{
    private readonly TwinValidationHandler _handler;

    public TwinValidationsController(TwinValidationHandler handler) => _handler = handler;

    [HttpPost]
    public async Task<IActionResult> Create(TwinValidationCreateDto request)
    {
        var result = await _handler.HandleAsync(request);
        return result.Status switch
        {
            ApplicationStatus.Ok => Ok(result.Value),
            ApplicationStatus.Accepted => Accepted(result.Value),
            _ => BadRequest(new { error = result.Error })
        };
    }
}
