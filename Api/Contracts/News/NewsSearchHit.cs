using System.Text.Json.Serialization;
using ContextService.Contracts.Materials;

namespace ContextService.Contracts.News;

// Canonical MaterialVersion metadata; stored text is intentionally absent.
public sealed record NewsSearchHit(
    string MaterialId, string VersionId, string SourceId, string Kind,
    string Title, DateTimeOffset KnownAt, DateTimeOffset? PublishedAt,
    string? CorrectsVersionId, Guid[] CompanyIds, string[] BankProfileIds,
    string ContentHash,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MaterialProvenance? Provenance = null);
