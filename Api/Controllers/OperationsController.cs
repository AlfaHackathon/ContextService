using AlfaHackaton.Controllers.Controllers;
using ContextService.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContextService.Controllers;

[ApiController]
public sealed class OperationsController(ContextChangeService changes) : BaseController
{
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { service = "context-service", status = "ok" });
    [HttpGet("context-service/internal/changes")]
    public async Task<IActionResult> Changes([FromQuery] long? after)
    {
        var result = await changes.Read(after ?? 0);
        return result.Error is not null ? OkOrProblem(result) : Ok(new
        {
            changes = result.Content
        });
    }
}
