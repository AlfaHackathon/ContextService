using Microsoft.AspNetCore.Mvc;
namespace ContextService.CsvImport;

// Protected by the existing /context-service/internal middleware. No provider call.
[ApiController]
public sealed class CsvReadinessController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("context-service/internal/csv-import/readiness")]
    public IActionResult Ready() => Ok(new { contract=1, declaredSourceSupported=true,
        workerEnabled=configuration.GetValue<bool>("Qdrant:WorkerEnabled") });
}
