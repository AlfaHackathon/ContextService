using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using ContextService.Contracts.Changes;
using ContextService.Models;
using ContextService.Resources;
using System.Text.Json;

namespace ContextService.Repositories;

public sealed class ContextChanges(IDapperContext context)
{
    private sealed class ChangeRow
    {
        public long sequence { get; set; }
        public string category { get; set; } = "";
        public string entity_id { get; set; } = "";
        public string version_id { get; set; } = "";
        public string known_at { get; set; } = "";
        public string company_ids { get; set; } = "";
    }
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
        if (after < 0) throw new ApiException(400, "invalid_change_cursor", "Change cursor must be nonnegative.");
        return (await context.ListOrEmpty<ChangeRow>(new QueryObject(ContextSql.ReadNextChange, new { after })))
            .Select(row => new ContextChange(row.sequence, row.category, row.entity_id, row.version_id,
                DateTimeOffset.Parse(row.known_at, System.Globalization.CultureInfo.InvariantCulture), JsonSerializer.Deserialize<Guid[]>(row.company_ids)!)).ToArray();
    }
}
