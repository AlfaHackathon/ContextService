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
    public async Task<IActionResult> Changes([FromQuery] long? after) => Ok(new { changes = await changes.Read(after ?? 0) });
}
