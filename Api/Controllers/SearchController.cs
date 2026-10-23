using ContextService.Contracts.Search;
using ContextService.Services;
using Microsoft.AspNetCore.Mvc;

namespace ContextService.Controllers;

[ApiController]
[Route("context-service/internal/search")]
public sealed class SearchController(SearchService search) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchRequest request, CancellationToken ct) => OkOrProblem<SearchResponse>(await search.Search(request, ct));
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken ct) => OkOrProblem(await search.Sync(ct));
}
