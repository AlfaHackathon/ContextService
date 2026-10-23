using System.Net;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Common;
using ContextService.Contracts.Knowledge;
using ContextService.Repositories;

namespace ContextService.Services;

public sealed class KnowledgeService(KnowledgeStore store, MaterialService materials)
{
    public async Task<Result<CompanyProfile>> SaveCompany(CompanyProfile p)
    {
        if (p.CompanyId == Guid.Empty ||
            string.IsNullOrWhiteSpace(p.Name) ||
            string.IsNullOrWhiteSpace(p.Description) ||
            p.IndustryCodes is null ||
            p.Exposures is null ||
            p.Description.Length > 16000)
        {
            return new(new Error("Company identity, name, bounded description and feature arrays are required.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        if ((await Validate(p.RevisionId, p.KnownAt, p.SourceRefs, p.CompanyId)).Error is { } companyEvidenceError)
        {
            return new(companyEvidenceError);
        }
        return await store.Save("company", p.CompanyId.ToString("D"), p.RevisionId, p.KnownAt, p with
        {
            KnownAt = p.KnownAt.ToUniversalTime()
        });
    }

    public async Task<Result<GroupProfile>> SaveGroup(GroupProfile p)
    {
        if (IdentifierValidation.RequireId(p.GroupId, "groupId").Error is { } groupIdError)
        {
            return new(groupIdError);
        }
        if (string.IsNullOrWhiteSpace(p.Name) ||
            p.Members is null ||
            p.Members.Length > 512)
        {
            return new(new Error("A group requires a name and at most 512 explicit members.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        if ((await Validate(p.RevisionId, p.KnownAt, p.SourceRefs)).Error is { } groupEvidenceError)
        {
            return new(groupEvidenceError);
        }
        if (p.AggregationRule is not (null or "sum_compatible_owner_effects"))
        {
            return new(new Error("Only an explicitly sourced sum_compatible_owner_effects rule is supported.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        var keys = new HashSet<string>();
        foreach (var member in p.Members)
        {
            if (member is null ||
                member.Kind is not ("company" or "group"))
            {
                return new(new Error("Each member must identify a company or a nested group.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
            }

            if (IdentifierValidation.RequireId(member.Id, "member id").Error is { } memberIdError)
            {
                return new(memberIdError);
            }
            if (member.Kind == "company" &&
                (!Guid.TryParse(member.Id, out var id) ||
                    id == Guid.Empty))
            {
                return new(new Error("Company members use application Guids.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
            }

            if (!keys.Add(member.Kind + ":" + member.Id.ToLowerInvariant()))
            {
                return new(new Error("A direct group edge may occur only once.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
            }

            if ((await Validate(p.RevisionId, p.KnownAt, member.SourceRefs)).Error is { } memberEvidenceError)
            {
                return new(memberEvidenceError);
            }
        }

        return await store.Save("group", p.GroupId, p.RevisionId, p.KnownAt, p with
        {
            KnownAt = p.KnownAt.ToUniversalTime()
        });
    }

    public async Task<Result<RoleProfile>> SaveRole(RoleProfile p)
    {
        if (IdentifierValidation.RequireId(p.PersonId, "personId").Error is { } personIdError)
        {
            return new(personIdError);
        }
        if (p.CompanyId == Guid.Empty ||
            string.IsNullOrWhiteSpace(p.Role) ||
            p.Responsibilities is null ||
            p.AllowedCompanyIds is null ||
            p.AllowedMaterialIds is null ||
            !p.AllowedCompanyIds.Contains(p.CompanyId) ||
            p.AllowedCompanyIds.Contains(Guid.Empty) ||
            p.AllowedCompanyIds.Length > 512 ||
            p.AllowedMaterialIds.Length > 2048)
        {
            return new(new Error("An explicit role, responsibilities and bounded read grant including its company are required.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        foreach (var id in p.AllowedMaterialIds)
        {
            if (IdentifierValidation.RequireId(id, "allowedMaterialId").Error is { } allowedMaterialIdError)
            {
                return new(allowedMaterialIdError);
            }
        }

        if ((await Validate(p.RevisionId, p.KnownAt, p.SourceRefs, p.CompanyId)).Error is { } bankEvidenceError)
        {
            return new(bankEvidenceError);
        }
        return await store.Save("role", p.PersonId + ":" + p.CompanyId.ToString("D"), p.RevisionId, p.KnownAt, p with
        {
            KnownAt = p.KnownAt.ToUniversalTime()
        });
    }

    public async Task<Result<PersonProfile>> SavePerson(PersonProfile p)
    {
        if (IdentifierValidation.RequireId(p.PersonId, "personId").Error is { } personIdError2)
        {
            return new(personIdError2);
        }
        if (p.CompanyLinks is null ||
            p.CompanyLinks.Length > 512)
        {
            return new(new Error("A person requires an explicit bounded links array.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        if ((await Validate(p.RevisionId, p.KnownAt, p.SourceRefs)).Error is { } personEvidenceError)
        {
            return new(personEvidenceError);
        }
        foreach (var link in p.CompanyLinks)
        {
            if (link.CompanyId == Guid.Empty ||
                link.RelationKind is not ("unspecified" or "employee" or "representative" or "owner"))
            {
                return new(new Error("A link requires an application company ID and an explicit relation kind.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
            }

            if ((await Validate(p.RevisionId, p.KnownAt, link.SourceRefs, link.CompanyId)).Error is { } linkEvidenceError)
            {
                return new(linkEvidenceError);
            }
        }

        return await store.Save("person", p.PersonId, p.RevisionId, p.KnownAt, p with
        {
            KnownAt = p.KnownAt.ToUniversalTime()
        });
    }

    public Task<PersonProfile[]> People(DateTimeOffset asOf) => store.List<PersonProfile>("person", asOf);
    public Task<CompanyProfile[]> Companies(DateTimeOffset asOf) => store.List<CompanyProfile>("company", asOf);
    public Task<CompanyProfile[]> CompanyVersions() => store.List<CompanyProfile>("company", DateTimeOffset.MaxValue, false);
    public Task<RoleProfile[]> Roles(DateTimeOffset asOf) => store.List<RoleProfile>("role", asOf);
    public Task<GroupProfile[]> Groups(DateTimeOffset asOf) => store.List<GroupProfile>("group", asOf);
    private async Task<Result> Validate(string revision, DateTimeOffset knownAt, SourceReference[] refs, Guid? company = null)
    {
        if (IdentifierValidation.RequireId(revision, "revisionId").Error is { } revisionIdError)
        {
            return new(revisionIdError);
        }
        if (knownAt == default ||
            refs is null ||
            refs.Length is < 1 or > 64)
        {
            return new(new Error("Knowledge time and 1–64 immutable source references are required.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        foreach (var r in refs)
        {
            if (r is null)
            {
                return new(new Error("Source references cannot contain null.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
            }

            var materialIdResolution = await materials.GetAsync(r.MaterialId, r.VersionId);
            if (materialIdResolution.Error is { } materialIdResolutionError)
            {
                return new(materialIdResolutionError);
            }

            var source = materialIdResolution.Content!;
            if (source.ContentHash != r.ContentHash ||
                source.KnownAt > knownAt)
            {
                return new(new Error("Evidence must match its original hash and be known at the profile revision.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.UnprocessableEntity });
            }

            if (company.HasValue &&
                !source.CompanyIds.Contains(company.Value))
            {
                return new(new Error("Company and role evidence must explicitly belong to the target company.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.UnprocessableEntity });
            }
        }

        return new();
    }

    public async Task<Result<GroupExpansion>> Expand(string id, DateTimeOffset asOf) => GroupExpansionService.Expand(id, await Groups(asOf));
}
