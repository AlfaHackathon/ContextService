using AlfaHackaton.Result;
using ContextService.Contracts.Companies;
using ContextService.Contracts.Materials;

namespace ContextService.Repositories.Interfaces;

public interface IMaterialRepository
{
    Task<Result<MaterialVersion>> Save(MaterialVersion material);
    Task<MaterialVersion?> GetAsync(string materialId, string versionId);
    Task<Result<CompanyEvidenceInventory>> CompanyEvidence(Guid companyId, DateTimeOffset asOf);
    Task<IReadOnlyList<MaterialVersion>> GetContextMaterials(Guid companyId, string newsMaterialId, DateTimeOffset asOf);
    Task<IReadOnlyList<MaterialVersion>> ListKnown(DateTimeOffset asOf);
}
