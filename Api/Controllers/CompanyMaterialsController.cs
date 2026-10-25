using AlfaHackaton.Controllers.Controllers;
using ContextService.Contracts.Materials;
using ContextService.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContextService.Controllers;

[ApiController]
[Route("context-service/internal/companies/{companyId:guid}/materials")]
public sealed class CompanyMaterialsController(CompanyMaterialListService materials) : BaseController
{
    [HttpGet]
    [ProducesResponseType(typeof(CompanyMaterialPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromRoute] Guid companyId, [FromQuery] CompanyMaterialListRequest request, CancellationToken ct) =>
        OkOrProblem<CompanyMaterialPage>(await materials.List(companyId, request, ct));
}
