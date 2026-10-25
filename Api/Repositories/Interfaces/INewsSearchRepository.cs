using ContextService.Contracts.News;

namespace ContextService.Repositories.Interfaces;

public interface INewsSearchRepository
{
    Task<NewsSearchHit[]> Search(string query, DateTimeOffset asOf, int limit, CancellationToken ct);
}
