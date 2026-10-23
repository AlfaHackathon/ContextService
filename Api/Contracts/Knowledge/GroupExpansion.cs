
namespace ContextService.Contracts.Knowledge;

public sealed record GroupExpansion(string GroupId, Guid[] CompanyIds, GroupPath[] Paths,
    string[] MissingGroups, string[][] Cycles, bool Complete, SourceReference[] SourceRefs, string? AggregationRule = null);

// Canonical, versioned business metadata. Qdrant remains a rebuildable search projection.
