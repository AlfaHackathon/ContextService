namespace ContextService.Contracts.Materials;

public sealed record CompanyMaterialPage(Guid CompanyId, DateTimeOffset AsOf,
    IReadOnlyList<CompanyMaterialSummary> Items, string? NextCursor,
    string Coverage = "canonical_changes_only");
