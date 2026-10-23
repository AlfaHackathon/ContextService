using ContextService.Contracts.Companies;
using ContextService.Contracts.Materials;

namespace ContextService.Repositories.Interfaces;

public interface IMaterialRepository
{
    Task<MaterialVersion> Save(MaterialVersion material);
    Task<MaterialVersion?> Get(string materialId, string versionId);
    Task<CompanyEvidenceInventory> CompanyEvidence(Guid companyId, DateTimeOffset asOf);
    Task<IReadOnlyList<MaterialVersion>> GetContextMaterials(Guid companyId, string newsMaterialId, DateTimeOffset asOf);
    Task<IReadOnlyList<MaterialVersion>> ListKnown(DateTimeOffset asOf);
}
