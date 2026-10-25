using System.Text.Json.Serialization;

namespace ContextService.Contracts.Materials;

// Explicit metadata allowlist: no original text or other company/bank scope identifiers.
public sealed record CompanyMaterialSummary(string MaterialId, string VersionId, string SourceId,
    string Kind, string Title, DateTimeOffset KnownAt, DateTimeOffset? PublishedAt, string? CorrectsVersionId,
    string ContentHash, string TextCompleteness, int LatestVersionCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MaterialProvenance? Provenance = null);
