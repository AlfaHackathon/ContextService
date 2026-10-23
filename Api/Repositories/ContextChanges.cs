using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using ContextService.Contracts.Changes;
using ContextService.Models;
using ContextService.Resources;

namespace ContextService.Repositories;

public sealed class ContextChanges(IDapperContext context)
{
    private sealed record ChangeRow(long Sequence, string Category, string EntityId, string VersionId, string KnownAt, string CompanyIds);
    internal static Task Append(ITransaction transaction, string category, string id, string version, DateTimeOffset knownAt, Guid[] companies) =>
        transaction.Command(new QueryObject(ContextSql.AppendChange, new
        {
            c = category,
            i = id,
            v = version,
            t = knownAt.ToString("O"),
            ticks = knownAt.UtcTicks,
            s = JsonSerializer.Serialize(companies)
        }));
    public async Task<ContextChange[]> Read(long after)
    {
        if (after < 0)
        {
            throw new ApiException(400, "invalid_change_cursor", "Change cursor must be nonnegative.");
        }

        return (await context.ListOrEmpty<ChangeRow>(new QueryObject(ContextSql.ReadNextChange, new
        {
            after
        })))
            .Select(row => new ContextChange(row.Sequence, row.Category, row.EntityId, row.VersionId,
                DateTimeOffset.Parse(row.KnownAt, System.Globalization.CultureInfo.InvariantCulture), JsonSerializer.Deserialize<Guid[]>(row.CompanyIds)!)).ToArray();
    }
}
