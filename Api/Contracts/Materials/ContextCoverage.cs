using ContextService.Contracts.Context;

namespace ContextService.Contracts.Materials;

public sealed record ContextCoverage(
    bool Complete, int MaterialCount, int CompanyMaterialCount,
    int MarketMaterialCount, int PreviousNewsVersionCount, int Characters,
    DateTimeOffset AsOf, ContextBudgetReport? Budget = null);
