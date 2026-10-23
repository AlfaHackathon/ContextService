using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlfaHackaton.Result;
using ContextService.Common;
using ContextService.Contracts.Companies;
using ContextService.Contracts.Materials;
using ContextService.Models;
using ContextService.Repositories.Interfaces;
using ContextService.Services.Interfaces;

namespace ContextService.Services;

public sealed class MaterialService(IMaterialRepository repository, int maxCharacters) : IMaterialService
{
    public static readonly JsonSerializerOptions Json = ContextJson.Options;
    public Task<Result<CompanyEvidenceInventory>> CompanyEvidence(Guid companyId, DateTimeOffset asOf) => repository.CompanyEvidence(companyId, asOf);
    public async Task<Result<MaterialVersion>> Save(MaterialVersionRequest request)
    {
        var normalization = MaterialValidation.Normalize(request);
        if (normalization.Error is { } normalizationError)
        {
            return new(normalizationError);
        }

        var normalized = normalization.Content!;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(normalized, Json))));
        return await repository.Save(new MaterialVersion(normalized.MaterialId,
            normalized.VersionId,
            normalized.SourceId,
            normalized.Kind,
            normalized.Title,
            normalized.Text,
            normalized.KnownAt,
            normalized.PublishedAt,
            normalized.CorrectsVersionId,
            normalized.CompanyIds,
            normalized.BankProfileIds,
            hash,
            normalized.Provenance));
    }

    public async Task<Result<MaterialVersion>> GetAsync(string materialId, string versionId)
    {
        if (RequireId(materialId, "materialId").Error is { } materialIdError)
        {
            return new(materialIdError);
        }
        if (RequireId(versionId, "versionId").Error is { } versionIdError)
        {
            return new(versionIdError);
        }
        var materialIdResolution = await repository.GetAsync(materialId, versionId);
        if (materialIdResolution is null)
        {
            return new(new ApiError(404, "material_not_found", "Material version was not found."));
        }

        return new(materialIdResolution, null);
    }

    public async Task<Result<ContextBundle>> GetContext(ContextRequest request)
    {
        if (request.CompanyId == Guid.Empty ||
            request.AsOf == default)
        {
            return new(new ApiError(400, "invalid_request", "companyId and asOf are required."));
        }

        var newsMaterialIdResolution = await GetAsync(request.NewsMaterialId, request.NewsVersionId);
        if (newsMaterialIdResolution.Error is { } newsMaterialIdResolutionError)
        {
            return new(newsMaterialIdResolutionError);
        }

        var news = newsMaterialIdResolution.Content!;
        if (news.Kind != "news")
        {
            return new(new ApiError(400, "not_news", "The requested version must be news."));
        }

        if (news.KnownAt > request.AsOf)
        {
            return new(new ApiError(403, "news_not_known", "The news version is not known at asOf."));
        }

        if (news.CompanyIds.Length > 0 &&
            !news.CompanyIds.Contains(request.CompanyId))
        {
            return new(new ApiError(403, "company_scope", "The news version belongs to a different company scope."));
        }

        var materials = (await repository.GetContextMaterials(request.CompanyId, request.NewsMaterialId, request.AsOf)).Where(m => m.MaterialId != news.MaterialId ||
                m.VersionId != news.VersionId).ToArray();
        var characters = JsonSerializer.Serialize(new
        {
            news,
            materials
        }, Json).Length;
        if (characters > maxCharacters)
        {
            return new(new ApiError(422,
                "context_budget_exceeded",
                $"Complete serialized source context requires {characters} characters; configured limit is {maxCharacters}. No partial context was returned."));
        }

        return new(new ContextBundle(news,
            materials,
            new ContextCoverage(true,
            materials.Length + 1,
            materials.Count(m => m.Kind == "company"),
            materials.Count(m => m.Kind == "market"),
            materials.Count(m => m.Kind == "news"),
            characters,
            request.AsOf.ToUniversalTime())),
            null);
    }

    public static Result RequireId(string? value, string name) => IdentifierValidation.RequireId(value, name);
}
