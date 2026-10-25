using AlfaHackaton.Controllers.Controllers;
using ContextService.Contracts.News;
using ContextService.Contracts.Materials;
using ContextService.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContextService.Controllers;

[ApiController]
[Route("context-service/internal/news")]
public class NewsController(NewsAdmissionService admission, NewsContextService news, NewsSearchService search) : BaseController
{
    [HttpPost("search")]
    [ProducesResponseType(typeof(NewsSearchResponse), StatusCodes.Status200OK)]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> SearchAsync([FromBody] NewsSearchRequest request, CancellationToken ct)
    {
        return OkOrProblem(await search.Search(request, ct));
    }

    [HttpPost("admit")]
    [ProducesResponseType(typeof(NewsAdmissionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdmitAsync([FromBody] NewsAdmissionRequest request, CancellationToken ct)
    {
        return OkOrProblem(await admission.AdmitAsync(request, ct));
    }

    [HttpPost("resolve")]
    [ProducesResponseType(typeof(MaterialVersion), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResolveAsync([FromBody] NewsResolutionRequest request, CancellationToken ct)
    {
        return OkOrProblem(await news.ResolveAsync(request, ct));
    }

    [HttpPost("public-sources")]
    [ProducesResponseType(typeof(PublicNewsSourcesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> PublicSourcesAsync([FromBody] PublicNewsSourcesRequest request, CancellationToken ct)
    {
        return OkOrProblem(await news.PublicSourcesAsync(request, ct));
    }
}
