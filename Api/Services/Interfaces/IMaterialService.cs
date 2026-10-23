using ContextService.Contracts.Companies;
using ContextService.Contracts.Materials;

namespace ContextService.Services.Interfaces;

public interface IMaterialService
{
    Task<MaterialVersion> Save(MaterialVersionRequest request);
    Task<MaterialVersion> Get(string materialId, string versionId);
    Task<ContextBundle> GetContext(ContextRequest request);
    Task<CompanyEvidenceInventory> CompanyEvidence(Guid companyId, DateTimeOffset asOf);
}
