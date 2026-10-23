using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using ContextService.Contracts.Companies;
using ContextService.Models;
using ContextService.Resources;

namespace ContextService.Repositories;

public sealed class CompanyEvidenceRepository(IDapperContext context)
{
    private sealed record EvidenceRow(string MaterialId, string VersionId, string ContentHash, string SourceId, long KnownTicks);
    public async Task<CompanyEvidenceInventory> CompanyEvidence(Guid companyId, DateTimeOffset asOf)
    {
        if (companyId == Guid.Empty || asOf == default)
        {
            throw new ApiException(400, "invalid_evidence_scope", "A company and knowledge time are required.");
        }

        using var transaction = context.BeginTransaction();
        // All canonical writes take the matching exclusive lock. Every read below
        // runs on this org transaction's connection, so rows and cursors share one snapshot.
        await transaction.Command(new QueryObject(ContextSql.ReadLock));
        var sequence = await transaction.CommandWithResponse<long>(new QueryObject(ContextSql.ChangeWatermark));
        var watchAfter = await transaction.CommandWithResponse<long>(new QueryObject(ContextSql.EvidenceWatchCursor, new
        {
            observed = sequence,
            at = asOf.UtcTicks
        }));
        var rows = await transaction.ListOrEmpty<EvidenceRow>(new QueryObject(ContextSql.CompanyEvidence,
            new
            {
                at = asOf.UtcTicks,
                company = companyId.ToString("D")
            }));
        transaction.Commit();
        return new(companyId, asOf.ToUniversalTime(), rows.Take(128).Select(row => new CompanyEvidenceSource(row.MaterialId,
            row.VersionId, row.ContentHash, row.SourceId, new DateTimeOffset(row.KnownTicks, TimeSpan.Zero))).ToArray(), rows.Count <= 128, sequence, watchAfter);
    }
}
