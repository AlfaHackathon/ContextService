using ContextService.Common;
using ContextService.Contracts.Knowledge;
using ContextService.Models;
using ContextService.Repositories;

namespace ContextService.Services;

public sealed class KnowledgeService(KnowledgeStore store, MaterialService materials)
{
    public async Task<CompanyProfile> SaveCompany(CompanyProfile p)
    {
        if (p.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Description)
            || p.IndustryCodes is null || p.Exposures is null || p.Description.Length > 16000)
            throw new ApiException(400, "invalid_company_profile", "Company identity, name, bounded description and feature arrays are required.");
        await Validate(p.RevisionId, p.KnownAt, p.SourceRefs, p.CompanyId);
        return await store.Save("company", p.CompanyId.ToString("D"), p.RevisionId, p.KnownAt, p with { KnownAt = p.KnownAt.ToUniversalTime() });
    }
    public async Task<GroupProfile> SaveGroup(GroupProfile p)
    {
        IdentifierValidation.RequireId(p.GroupId, "groupId");
        if (string.IsNullOrWhiteSpace(p.Name) || p.Members is null || p.Members.Length > 512)
            throw new ApiException(400, "invalid_group", "A group requires a name and at most 512 explicit members.");
        await Validate(p.RevisionId, p.KnownAt, p.SourceRefs);
        if (p.AggregationRule is not (null or "sum_compatible_owner_effects"))
            throw new ApiException(400, "invalid_aggregation_rule", "Only an explicitly sourced sum_compatible_owner_effects rule is supported.");
        var keys = new HashSet<string>();
        foreach (var member in p.Members)
        {
            if (member is null || member.Kind is not ("company" or "group"))
                throw new ApiException(400, "invalid_group_member", "Each member must identify a company or a nested group.");
            IdentifierValidation.RequireId(member.Id, "member id");
            if (member.Kind == "company" && (!Guid.TryParse(member.Id, out var id) || id == Guid.Empty))
                throw new ApiException(400, "invalid_company", "Company members use application Guids.");
            if (!keys.Add(member.Kind + ":" + member.Id.ToLowerInvariant()))
                throw new ApiException(400, "duplicate_group_member", "A direct group edge may occur only once.");
            await Validate(p.RevisionId, p.KnownAt, member.SourceRefs);
        }
        return await store.Save("group", p.GroupId, p.RevisionId, p.KnownAt, p with { KnownAt = p.KnownAt.ToUniversalTime() });
    }
    public async Task<RoleProfile> SaveRole(RoleProfile p)
    {
        IdentifierValidation.RequireId(p.PersonId, "personId");
        if (p.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(p.Role) || p.Responsibilities is null
            || p.AllowedCompanyIds is null || p.AllowedMaterialIds is null
            || !p.AllowedCompanyIds.Contains(p.CompanyId) || p.AllowedCompanyIds.Contains(Guid.Empty)
            || p.AllowedCompanyIds.Length > 512 || p.AllowedMaterialIds.Length > 2048)
            throw new ApiException(400, "invalid_role", "An explicit role, responsibilities and bounded read grant including its company are required.");
        foreach (var id in p.AllowedMaterialIds) IdentifierValidation.RequireId(id, "allowedMaterialId");
        await Validate(p.RevisionId, p.KnownAt, p.SourceRefs, p.CompanyId);
        return await store.Save("role", p.PersonId + ":" + p.CompanyId.ToString("D"), p.RevisionId, p.KnownAt,
            p with { KnownAt = p.KnownAt.ToUniversalTime() });
    }
    public async Task<PersonProfile> SavePerson(PersonProfile p)
    {
        IdentifierValidation.RequireId(p.PersonId, "personId");
        if (p.CompanyLinks is null || p.CompanyLinks.Length > 512)
            throw new ApiException(400, "invalid_person_links", "A person requires an explicit bounded links array.");
        await Validate(p.RevisionId, p.KnownAt, p.SourceRefs);
        foreach (var link in p.CompanyLinks)
        {
            if (link.CompanyId == Guid.Empty || link.RelationKind is not ("unspecified" or "employee" or "representative" or "owner"))
                throw new ApiException(400, "invalid_person_link", "A link requires an application company ID and an explicit relation kind.");
            await Validate(p.RevisionId, p.KnownAt, link.SourceRefs, link.CompanyId);
        }
        return await store.Save("person", p.PersonId, p.RevisionId, p.KnownAt, p with { KnownAt = p.KnownAt.ToUniversalTime() });
    }
    public Task<PersonProfile[]> People(DateTimeOffset asOf) => store.List<PersonProfile>("person", asOf);
    public Task<CompanyProfile[]> Companies(DateTimeOffset asOf) => store.List<CompanyProfile>("company", asOf);
    public Task<CompanyProfile[]> CompanyVersions() => store.List<CompanyProfile>("company", DateTimeOffset.MaxValue, false);
    public Task<RoleProfile[]> Roles(DateTimeOffset asOf) => store.List<RoleProfile>("role", asOf);
    public Task<GroupProfile[]> Groups(DateTimeOffset asOf) => store.List<GroupProfile>("group", asOf);

    private async Task Validate(string revision, DateTimeOffset knownAt, SourceReference[] refs, Guid? company = null)
    {
        IdentifierValidation.RequireId(revision, "revisionId");
        if (knownAt == default || refs is null || refs.Length is < 1 or > 64)
            throw new ApiException(400, "knowledge_evidence_required", "Knowledge time and 1–64 immutable source references are required.");
        foreach (var r in refs)
        {
            if (r is null) throw new ApiException(400, "invalid_source", "Source references cannot contain null.");
            var source = await materials.Get(r.MaterialId, r.VersionId);
            if (source.ContentHash != r.ContentHash || source.KnownAt > knownAt)
                throw new ApiException(422, "invalid_knowledge_evidence", "Evidence must match its original hash and be known at the profile revision.");
            if (company.HasValue && !source.CompanyIds.Contains(company.Value))
                throw new ApiException(422, "profile_company_scope", "Company and role evidence must explicitly belong to the target company.");
        }
    }
    public async Task<GroupExpansion> Expand(string id, DateTimeOffset asOf) => GroupExpansionService.Expand(id, await Groups(asOf));
}
