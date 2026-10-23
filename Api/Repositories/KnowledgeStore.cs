using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Common;
using ContextService.Contracts.Knowledge;
using ContextService.Resources;
using Npgsql;

namespace ContextService.Repositories;

public sealed class KnowledgeStore(IDapperContext context)
{
    public async Task<Result<T>> Save<T>(string category, string id, string revision, DateTimeOffset knownAt, T value)
    {
        var json = JsonSerializer.Serialize(value, ContextJson.Options);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        using var transaction = context.BeginTransaction();
        await transaction.Command(new QueryObject(ContextSql.WriteLock));
        var old = await transaction.FirstOrDefault<string>(new QueryObject(ContextSql.FindKnowledgeHash, new
        {
            c = category,
            e = id,
            r = revision
        }));
        if (old is not null)
        {
            if (old != hash)
            {
                return new(new Error("Knowledge revisions are immutable.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.Conflict });
            }

            transaction.Commit();
            return new(value, null);
        }

        try
        {
            await transaction.Command(new QueryObject(ContextSql.InsertKnowledge,
                new
                {
                    c = category,
                    e = id,
                    r = revision,
                    t = knownAt.UtcTicks,
                    h = hash,
                    p = json
                }));
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return new(new Error("Two different revisions cannot share one knowledge time.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.Conflict });
        }

        var companies = value switch
        {
            CompanyProfile p => new[]
            {
                p.CompanyId
            },
            RoleProfile p => p.AllowedCompanyIds,
            _ => Array.Empty<Guid>()
        };
        await ContextChanges.Append(transaction, category, id, revision, knownAt, companies);
        transaction.Commit();
        return new(value, null);
    }

    public async Task<T[]> List<T>(string category, DateTimeOffset asOf, bool latestOnly = true) => (await context.ListOrEmpty<string>(new QueryObject(latestOnly ? ContextSql.LatestKnowledge : ContextSql.AllKnowledge, new { c = category, t = asOf.UtcTicks }))).Select(json => JsonSerializer.Deserialize<T>(json, ContextJson.Options)!).ToArray();
}
