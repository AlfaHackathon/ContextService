using ContextService.Contracts.Knowledge;

namespace ContextService.Contracts.Search;

public sealed record SearchHit(Guid CompanyId, SourceReference[] SourceRefs, double Score,
    string? Excerpt, string Kind, string? MaterialId, string? VersionId,
    SourceReference[]? RelatedVersions = null);
