
namespace ContextService.Contracts.Knowledge;

public sealed record RoleProfile(string PersonId, Guid CompanyId, string RevisionId, string Role,
    string[] Responsibilities, Guid[] AllowedCompanyIds, string[] AllowedMaterialIds,
    DateTimeOffset KnownAt, SourceReference[] SourceRefs);
