using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using ContextService.Resources;

namespace ContextService.Repositories;

public sealed class SearchProjectionRepository(IDapperContext context)
{
    private sealed class ProjectionRow
    {
        public string item_id { get; set; } = "";
        public string content_hash { get; set; } = "";
    }
    public async Task<Dictionary<string, string>> Read(string pipeline) =>
        (await context.ListOrEmpty<ProjectionRow>(new QueryObject(ContextSql.ProjectionMarks, new
        {
            p = pipeline
        })))
            .ToDictionary(row => row.item_id, row => row.content_hash);
    public async Task Mark(string pipeline, IEnumerable<KeyValuePair<string, string>> entries)
    {
        using var transaction = context.BeginTransaction();
        await transaction.Command(new QueryObject(ContextSql.WriteLock));
        foreach (var entry in entries)
        {
            await transaction.Command(new QueryObject(ContextSql.UpsertProjectionMark,
                new
                {
                    p = pipeline,
                    i = entry.Key,
                    h = entry.Value
                }));
        }

        transaction.Commit();
    }
}
