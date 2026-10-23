namespace ContextService.Contracts.Search;

public sealed record SearchProjectionResult(int Indexed, int CanonicalEntries, string Mode, string Collection);
