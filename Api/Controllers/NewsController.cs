using AlfaHackaton.Controllers.Controllers;
using ContextService.Contracts.News;
using ContextService.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContextService.Controllers;

[ApiController]
[Route("context-service/internal/news")]
public class NewsController(NewsAdmissionService admission, NewsContextService news) : BaseController
{
    [HttpPost("admit")]
    public async Task<IActionResult> AdmitAsync([FromBody] NewsAdmissionRequest request, CancellationToken ct)
    {
        return OkOrProblem(await admission.AdmitAsync(request, ct));
    }

    [HttpPost("resolve")]
    public async Task<IActionResult> ResolveAsync([FromBody] NewsResolutionRequest request, CancellationToken ct)
    {
        return OkOrProblem(await news.ResolveAsync(request, ct));
    }

    [HttpPost("public-sources")]
    public async Task<IActionResult> PublicSourcesAsync([FromBody] PublicNewsSourcesRequest request, CancellationToken ct)
    {
        return OkOrProblem(await news.PublicSourcesAsync(request, ct));
    }
}
