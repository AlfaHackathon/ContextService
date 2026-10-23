using ContextService.Contracts.Knowledge;

namespace ContextService.Models;

internal sealed record IndexEntry(string Id, string Text, DateTimeOffset KnownAt, Guid[] CompanyIds,
    string Kind, string? MaterialId, string? VersionId, string Hash, SourceReference[] SourceRefs);
