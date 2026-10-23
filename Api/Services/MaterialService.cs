using ContextService.Common;
using ContextService.Contracts.Companies;
using ContextService.Services.Interfaces;
using ContextService.Contracts.Materials;
using ContextService.Models;
using ContextService.Repositories.Interfaces;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace ContextService.Services;

public sealed class MaterialService(IMaterialRepository repository, int maxCharacters) : IMaterialService
{
    public static readonly JsonSerializerOptions Json = ContextJson.Options;
    public Task<CompanyEvidenceInventory> CompanyEvidence(Guid companyId, DateTimeOffset asOf) => repository.CompanyEvidence(companyId, asOf);

    public async Task<MaterialVersion> Save(MaterialVersionRequest request)
    {
        var normalized = MaterialValidation.Normalize(request);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(normalized, Json))));
        return await repository.Save(new MaterialVersion(normalized.MaterialId, normalized.VersionId,
            normalized.SourceId, normalized.Kind, normalized.Title, normalized.Text,
            normalized.KnownAt, normalized.PublishedAt, normalized.CorrectsVersionId,
            normalized.CompanyIds, normalized.BankProfileIds, hash, normalized.Provenance));
    }

    public async Task<MaterialVersion> Get(string materialId, string versionId)
    {
        RequireId(materialId, "materialId");
        RequireId(versionId, "versionId");
        return await repository.Get(materialId, versionId)
            ?? throw new ApiException(404, "material_not_found", "Material version was not found.");
    }

    public async Task<ContextBundle> GetContext(ContextRequest request)
    {
        if (request.CompanyId == Guid.Empty || request.AsOf == default)
            throw new ApiException(400, "invalid_request", "companyId and asOf are required.");
        var news = await Get(request.NewsMaterialId, request.NewsVersionId);
        if (news.Kind != "news")
            throw new ApiException(400, "not_news", "The requested version must be news.");
        if (news.KnownAt > request.AsOf)
            throw new ApiException(403, "news_not_known", "The news version is not known at asOf.");
        if (news.CompanyIds.Length > 0 && !news.CompanyIds.Contains(request.CompanyId))
            throw new ApiException(403, "company_scope", "The news version belongs to a different company scope.");
        var materials = (await repository.GetContextMaterials(request.CompanyId, request.NewsMaterialId, request.AsOf))
            .Where(m => m.MaterialId != news.MaterialId || m.VersionId != news.VersionId).ToArray();
        var characters = JsonSerializer.Serialize(new { news, materials }, Json).Length;
        if (characters > maxCharacters)
            throw new ApiException(422, "context_budget_exceeded",
                $"Complete serialized source context requires {characters} characters; configured limit is {maxCharacters}. No partial context was returned.");
        return new ContextBundle(news, materials, new ContextCoverage(true, materials.Length + 1,
            materials.Count(m => m.Kind == "company"), materials.Count(m => m.Kind == "market"),
            materials.Count(m => m.Kind == "news"), characters, request.AsOf.ToUniversalTime()));
    }



    public static void RequireId(string? value, string name) => IdentifierValidation.RequireId(value, name);
}
