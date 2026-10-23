
namespace ContextService.Contracts.Changes;

public sealed record ContextChange(long Sequence, string Category, string EntityId, string VersionId,
    DateTimeOffset KnownAt, Guid[] CompanyIds);

// Written in the SAME transaction as canonical material/knowledge writes. The
// consumer advances its own durable cursor only after enqueueing durable work.
