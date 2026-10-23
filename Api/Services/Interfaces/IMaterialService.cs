using AlfaHackaton.Result;
using ContextService.Contracts.Companies;
using ContextService.Contracts.Materials;

namespace ContextService.Services.Interfaces;

public interface IMaterialService
{
    Task<Result<MaterialVersion>> Save(MaterialVersionRequest request);
    Task<Result<MaterialVersion>> GetAsync(string materialId, string versionId);
    Task<Result<ContextBundle>> GetContext(ContextRequest request);
    Task<Result<CompanyEvidenceInventory>> CompanyEvidence(Guid companyId, DateTimeOffset asOf);
}
