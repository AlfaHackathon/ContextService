using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using ContextService.Common;
using ContextService.Contracts.Materials;
using ContextService.Repositories.Interfaces;
using ContextService.Resources;

namespace ContextService.Repositories;

public sealed class CompanyMaterialListRepository(IDapperContext context) : ICompanyMaterialListRepository
{
    public async Task<CompanyMaterialSlice> List(Guid companyId, DateTimeOffset asOf, string? bankProfileId,
        int limit, CompanyMaterialContinuation? continuation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var transaction = context.BeginTransaction();
        await transaction.Command(new QueryObject(ContextSql.CompanyMaterialListDeadline));
        // Canonical writers take the exclusive lock before material+change-feed commit.
        // Capture one watermark; subsequent pages reuse it even after backdated admissions.
        await transaction.Command(new QueryObject(ContextSql.ReadLock));
        var current = await transaction.CommandWithResponse<long>(new QueryObject(ContextSql.ChangeWatermark));
        var snapshot = continuation?.SnapshotSequence ?? current;
        if (snapshot > current)
        {
            throw new BadHttpRequestException("The materials cursor is ahead of canonical storage.");
        }
        var rows = await transaction.ListOrEmpty<string>(new QueryObject(ContextSql.CompanyMaterialList, new
        {
            company = companyId.ToString("D"), at = asOf.UtcTicks, bank = bankProfileId, snapshot,
            take = limit + 1, after = continuation?.After.KnownTicks,
            material = continuation?.After.MaterialId, version = continuation?.After.VersionId
        }));
        ct.ThrowIfCancellationRequested();
        var items = rows.Select(json => JsonSerializer.Deserialize<CompanyMaterialSummary>(json, ContextJson.Options)
            ?? throw new JsonException("Invalid canonical company material metadata.")).ToArray();
        transaction.Commit();
        return new(snapshot, items);
    }
}
