using System.Text.Json.Serialization;

namespace ContextService.Contracts.Materials;

public sealed record MaterialVersionRequest(
    string MaterialId, string VersionId, string SourceId, string Kind,
    string Title, string Text, DateTimeOffset KnownAt, DateTimeOffset? PublishedAt,
    string? CorrectsVersionId, Guid[] CompanyIds, string[] BankProfileIds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MaterialProvenance? Provenance = null);
