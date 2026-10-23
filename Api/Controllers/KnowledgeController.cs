using AlfaHackaton.Controllers.Controllers;
using ContextService.Contracts.Knowledge;
using ContextService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ContextService.Controllers;

[ApiController]
[Route("context-service/internal")]
public sealed class KnowledgeController(CompanyKnowledgeClient knowledge) : BaseController
{
    [HttpPost("companies")]
    public async Task<IActionResult> SaveCompany([FromBody] CompanyProfile request, CancellationToken ct) => OkOrProblem<CompanyProfile>(await knowledge.SaveCompany(request, ct));
    [HttpGet("companies")]
    public async Task<IActionResult> Companies([FromQuery, BindRequired] DateTimeOffset asOf, CancellationToken ct)
    {
        var result = await knowledge.Companies(asOf, ct);
        return result.Error is { } error ? OkOrProblem<CompanyProfile[]>(new(error)) : Ok(new
        {
            companies = result.Content
        });
    }
    [HttpPost("groups")]
    public async Task<IActionResult> SaveGroup([FromBody] GroupProfile request, CancellationToken ct) => OkOrProblem<GroupProfile>(await knowledge.SaveGroup(request, ct));
    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery, BindRequired] DateTimeOffset asOf, CancellationToken ct)
    {
        var result = await knowledge.Groups(asOf, ct);
        return result.Error is { } error ? OkOrProblem<GroupProfile[]>(new(error)) : Ok(new
        {
            groups = result.Content
        });
    }
    [HttpGet("groups/{groupId}")]
    public async Task<IActionResult> Expand([FromRoute] string groupId, [FromQuery, BindRequired] DateTimeOffset asOf, CancellationToken ct) => OkOrProblem<GroupExpansion>(await knowledge.Expand(groupId, asOf, ct));
    [HttpPost("roles")]
    public async Task<IActionResult> SaveRole([FromBody] RoleProfile request, CancellationToken ct) => OkOrProblem<RoleProfile>(await knowledge.SaveRole(request, ct));
    [HttpGet("roles")]
    public async Task<IActionResult> Roles([FromQuery, BindRequired] DateTimeOffset asOf, CancellationToken ct)
    {
        var result = await knowledge.Roles(asOf, ct);
        return result.Error is { } error ? OkOrProblem<RoleProfile[]>(new(error)) : Ok(new
        {
            roles = result.Content
        });
    }
    [HttpPost("people")]
    public async Task<IActionResult> SavePerson([FromBody] PersonProfile request, CancellationToken ct) => OkOrProblem<PersonProfile>(await knowledge.SavePerson(request, ct));
    [HttpGet("people/{personId}")]
    public async Task<IActionResult> Person([FromRoute] string personId, [FromQuery, BindRequired] DateTimeOffset asOf, CancellationToken ct) => OkOrProblem<PersonProfile>(await knowledge.Person(personId, asOf, ct));
}
