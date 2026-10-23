
namespace ContextService.Contracts.Knowledge;

public sealed record PersonCompanyLink(Guid CompanyId, string RelationKind, SourceReference[] SourceRefs);
