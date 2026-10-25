namespace ContextService.Contracts.News;

public sealed record NewsSearchResponse(DateTimeOffset AsOf, bool Exhaustive, NewsSearchHit[] Hits);
