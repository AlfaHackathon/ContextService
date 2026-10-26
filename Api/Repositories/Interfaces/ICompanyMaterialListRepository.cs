using ContextService.Contracts.Materials;

namespace ContextService.Repositories.Interfaces;

public sealed record CompanyMaterialPosition(long KnownTicks, string MaterialId, string VersionId);
public sealed record CompanyMaterialContinuation(long SnapshotSequence, CompanyMaterialPosition After);
public sealed record CompanyMaterialSlice(long SnapshotSequence, IReadOnlyList<CompanyMaterialSummary> Items);

public interface ICompanyMaterialListRepository
{
    // Up to limit+1 rows for bounded lookahead; the service owns the opaque scoped cursor.
    Task<CompanyMaterialSlice> List(Guid companyId, DateTimeOffset asOf, string? bankProfileId,
        int limit, CompanyMaterialContinuation? continuation, CancellationToken ct);
}
