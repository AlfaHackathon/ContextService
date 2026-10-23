using ContextService.Contracts.Knowledge;
using ContextService.Models;
using ContextService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ContextService.Controllers;

[ApiController]
[Route("context-service/internal")]
public sealed class KnowledgeController(KnowledgeService knowledge) : ApiControllerBase
{
    [HttpPost("companies")]
    public async Task<IActionResult> SaveCompany([FromBody] CompanyProfile request) => OkOrProblem<CompanyProfile>(await knowledge.SaveCompany(request));
    [HttpGet("companies")]
    public async Task<IActionResult> Companies([FromQuery, BindRequired] DateTimeOffset asOf) => Ok(new { companies = await knowledge.Companies(asOf) });
    [HttpPost("groups")]
    public async Task<IActionResult> SaveGroup([FromBody] GroupProfile request) => OkOrProblem<GroupProfile>(await knowledge.SaveGroup(request));
    [HttpGet("groups")]
    public async Task<IActionResult> Groups([FromQuery, BindRequired] DateTimeOffset asOf) => Ok(new { groups = await knowledge.Groups(asOf) });
    [HttpGet("groups/{groupId}")]
    public async Task<IActionResult> Expand([FromRoute] string groupId, [FromQuery, BindRequired] DateTimeOffset asOf) => OkOrProblem<GroupExpansion>(await knowledge.Expand(groupId, asOf));
    [HttpPost("roles")]
    public async Task<IActionResult> SaveRole([FromBody] RoleProfile request) => OkOrProblem<RoleProfile>(await knowledge.SaveRole(request));
    [HttpGet("roles")]
    public async Task<IActionResult> Roles([FromQuery, BindRequired] DateTimeOffset asOf) => Ok(new { roles = await knowledge.Roles(asOf) });
    [HttpPost("people")]
    public async Task<IActionResult> SavePerson([FromBody] PersonProfile request) => OkOrProblem<PersonProfile>(await knowledge.SavePerson(request));
    [HttpGet("people/{personId}")]
    public async Task<IActionResult> Person([FromRoute] string personId, [FromQuery, BindRequired] DateTimeOffset asOf)
    {
        var person = (await knowledge.People(asOf)).SingleOrDefault(person => person.PersonId == personId);
        return person is null
            ? OkOrProblem<PersonProfile>(new(new ApiError(404, "person_not_found", "Person profile is unavailable at asOf.")))
            : OkOrProblem<PersonProfile>(new(person, null));
    }
}
