namespace ContextService.Contracts.Knowledge;

// Operational declarations guide relevance; they do not establish documentary financial exposure.
public sealed record DeclaredCompanyProfile(string? Region, string[]? IndustrySections,
    DeclaredCompanyRevenue? Revenue, int? CountOfEmployees, SourceReference[] SourceRefs);

public sealed record DeclaredCompanyRevenue(string Amount, string? Currency, string? Period);
