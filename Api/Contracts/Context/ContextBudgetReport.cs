using ContextService.Contracts.Knowledge;

namespace ContextService.Contracts.Context;

public sealed record ContextBudgetReport(int MaximumCharacters, int UsedCharacters,
    SourceReference[] OmittedSources, string StopReason);
