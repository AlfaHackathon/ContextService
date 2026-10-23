
namespace ContextService.Contracts.Search;

public sealed record SearchResponse(SearchHit[] Hits, string Mode, string Reranker, bool Exhaustive,
    int CandidatesExamined, DateTimeOffset AsOf);
