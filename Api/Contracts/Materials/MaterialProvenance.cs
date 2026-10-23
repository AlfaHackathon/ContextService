
namespace ContextService.Contracts.Materials;

public sealed record MaterialProvenance(string Acquisition, string? SourceUrl, DateTimeOffset RetrievedAt,
    string TextCompleteness, string? OriginVersionId = null, string? Locator = null);
