using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using ContextService.Common;
using ContextService.Contracts.News;
using ContextService.Repositories.Interfaces;
using ContextService.Resources;

namespace ContextService.Repositories;

public sealed class NewsSearchRepository(IDapperContext context) : INewsSearchRepository
{
    public async Task<NewsSearchHit[]> Search(string query, DateTimeOffset asOf, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var transaction = context.BeginTransaction();
        // The org DB API has no cancellation parameter; a server-side deadline also
        // bounds work after the HTTP client disconnects. No timeout becomes success.
        await transaction.Command(new QueryObject(ContextSql.NewsSearchDeadline));
        var rows = await transaction.ListOrEmpty<string>(new QueryObject(ContextSql.NewsSearch, new
        {
            query,
            at = asOf.UtcTicks,
            limit
        }));
        ct.ThrowIfCancellationRequested();
        var hits = rows.Select(json => JsonSerializer.Deserialize<NewsSearchHit>(json, ContextJson.Options)
            ?? throw new JsonException("Invalid canonical news metadata.")).ToArray();
        transaction.Commit();
        return hits;
    }
}
