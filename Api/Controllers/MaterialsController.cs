using ContextService.Contracts.Companies;
using AlfaHackaton.Controllers.Controllers;
using ContextService.Contracts.Materials;
using ContextService.Contracts.Retrieval;
using ContextService.Services.Interfaces;
using ContextService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ContextService.Controllers;

[ApiController]
[Route("context-service/internal")]
public sealed class MaterialsController(IMaterialService materials, RetrievedContextService retrieved) : BaseController
{
    [HttpPost("material-versions")]
    public async Task<IActionResult> Save([FromBody] MaterialVersionRequest request) => OkOrProblem<MaterialVersion>(await materials.Save(request));

    [HttpGet("material-versions/{materialId}/{versionId}")]
    public async Task<IActionResult> Get([FromRoute] string materialId, [FromRoute] string versionId) => OkOrProblem<MaterialVersion>(await materials.Get(materialId, versionId));

    [HttpPost("context")]
    public async Task<IActionResult> Context([FromBody] ContextRequest request) => OkOrProblem<ContextBundle>(await materials.GetContext(request));

    [HttpPost("context/retrieve")]
    public async Task<IActionResult> Retrieve([FromBody] RetrieveContextRequest request, CancellationToken ct) => OkOrProblem<RetrievedContextBundle>(await retrieved.Get(request, ct));

    [HttpGet("companies/{companyId:guid}/evidence")]
    public async Task<IActionResult> Evidence([FromRoute] Guid companyId, [FromQuery, BindRequired] DateTimeOffset asOf) => OkOrProblem<CompanyEvidenceInventory>(await materials.CompanyEvidence(companyId, asOf));
}
