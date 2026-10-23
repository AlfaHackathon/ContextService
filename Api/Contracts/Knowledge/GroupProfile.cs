
namespace ContextService.Contracts.Knowledge;

public sealed record GroupProfile(string GroupId, string RevisionId, string Name,
    DateTimeOffset KnownAt, GroupMember[] Members, SourceReference[] SourceRefs, string? AggregationRule = null);
