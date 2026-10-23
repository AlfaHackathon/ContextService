using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using ContextService.Common;
using ContextService.Contracts.Knowledge;
using ContextService.Models;
using ContextService.Resources;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ContextService.Repositories;

public sealed class KnowledgeStore(IDapperContext context)
{
    public async Task<T> Save<T>(string category, string id, string revision, DateTimeOffset knownAt, T value)
    {
        var json = JsonSerializer.Serialize(value, ContextJson.Options);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        using var transaction = context.BeginTransaction();
        await transaction.Command(new QueryObject(ContextSql.WriteLock));
        var old = await transaction.CommandWithResponse<string?>(new QueryObject(ContextSql.FindKnowledgeHash, new { c = category, e = id, r = revision }));
        if (old is not null)
        {
            if (old != hash) throw new ApiException(409, "knowledge_revision_conflict", "Knowledge revisions are immutable.");
            transaction.Commit();
            return value;
        }
        try
        {
            await transaction.Command(new QueryObject(ContextSql.InsertKnowledge,
                new { c = category, e = id, r = revision, t = knownAt.UtcTicks, h = hash, p = json }));
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        { throw new ApiException(409, "knowledge_time_conflict", "Two different revisions cannot share one knowledge time."); }
        var companies = value switch { CompanyProfile p => new[] { p.CompanyId }, RoleProfile p => p.AllowedCompanyIds, _ => Array.Empty<Guid>() };
        await ContextChanges.Append(transaction, category, id, revision, knownAt, companies);
        transaction.Commit();
        return value;
    }
    public async Task<T[]> List<T>(string category, DateTimeOffset asOf, bool latestOnly = true) =>
        (await context.ListOrEmpty<string>(new QueryObject(latestOnly ? ContextSql.LatestKnowledge : ContextSql.AllKnowledge, new { c = category, t = asOf.UtcTicks })))
            .Select(json => JsonSerializer.Deserialize<T>(json, ContextJson.Options)!).ToArray();
}
