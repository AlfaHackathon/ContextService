using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using ContextService.Common;
using ContextService.Contracts.Companies;
using ContextService.Contracts.Materials;
using ContextService.Models;
using ContextService.Repositories.Interfaces;
using ContextService.Resources;
using System.Text.Json;

namespace ContextService.Repositories;

public sealed class PostgresMaterialRepository(IDapperContext context, CompanyEvidenceRepository evidence) : IMaterialRepository
{
    public async Task<MaterialVersion> Save(MaterialVersion material)
    {
        using var transaction = context.BeginTransaction();
        await transaction.Command(new QueryObject(ContextSql.WriteLock));
        var existingJson = await transaction.CommandWithResponse<string?>(new QueryObject(ContextSql.FindMaterial,
            new { material = material.MaterialId, version = material.VersionId }));
        if (existingJson is not null)
        {
            var existing = JsonSerializer.Deserialize<MaterialVersion>(existingJson, ContextJson.Options)!;
            if (existing.ContentHash != material.ContentHash)
                throw new ApiException(409, "immutable_version_conflict", "This material/version already exists with different content or metadata.");
            transaction.Commit();
            return existing;
        }
        var identityJson = await transaction.CommandWithResponse<string?>(new QueryObject(ContextSql.FindMaterialIdentity, new { material = material.MaterialId }));
        if (identityJson is not null)
        {
            using var identity = JsonDocument.Parse(identityJson);
            if (identity.RootElement.GetProperty("source_id").GetString() != material.SourceId || identity.RootElement.GetProperty("kind").GetString() != material.Kind)
                throw new ApiException(409, "material_identity_conflict", "sourceId and kind cannot change within one material identity.");
        }
        if (material.CorrectsVersionId is not null)
        {
            var priorJson = await transaction.CommandWithResponse<string?>(new QueryObject(ContextSql.FindMaterial,
                new { material = material.MaterialId, version = material.CorrectsVersionId }));
            var prior = priorJson is null ? null : JsonSerializer.Deserialize<MaterialVersion>(priorJson, ContextJson.Options);
            if (prior is null) throw new ApiException(409, "missing_corrected_version", "The corrected version must already exist on this material.");
            if (prior.KnownAt > material.KnownAt)
                throw new ApiException(409, "correction_knowledge_order", "A correction cannot be known before its predecessor.");
        }
        await transaction.Command(new QueryObject(ContextSql.InsertMaterial, new
        {
            material = material.MaterialId,
            version = material.VersionId,
            source = material.SourceId,
            kind = material.Kind,
            known = material.KnownAt.UtcTicks,
            corrects = material.CorrectsVersionId,
            hash = material.ContentHash,
            json = JsonSerializer.Serialize(material, ContextJson.Options)
        }));
        foreach (var company in material.CompanyIds)
            await transaction.Command(new QueryObject(ContextSql.InsertMaterialCompany,
                new { material = material.MaterialId, version = material.VersionId, company = company.ToString("D") }));
        await ContextChanges.Append(transaction, "material:" + material.Kind, material.MaterialId, material.VersionId, material.KnownAt, material.CompanyIds);
        transaction.Commit();
        return material;
    }
    public async Task<MaterialVersion?> Get(string materialId, string versionId)
    {
        var json = await context.FirstOrDefault<string>(new QueryObject(ContextSql.FindMaterial, new { material = materialId, version = versionId }));
        return json is null ? null : JsonSerializer.Deserialize<MaterialVersion>(json, ContextJson.Options);
    }
    public Task<CompanyEvidenceInventory> CompanyEvidence(Guid companyId, DateTimeOffset asOf) => evidence.CompanyEvidence(companyId, asOf);
    public async Task<IReadOnlyList<MaterialVersion>> GetContextMaterials(Guid companyId, string newsMaterialId, DateTimeOffset asOf) =>
        (await context.ListOrEmpty<string>(new QueryObject(ContextSql.ContextMaterials,
            new { asof = asOf.UtcTicks, company = companyId.ToString("D"), news = newsMaterialId })))
        .Select(json => JsonSerializer.Deserialize<MaterialVersion>(json, ContextJson.Options)!).ToArray();
    public async Task<IReadOnlyList<MaterialVersion>> ListKnown(DateTimeOffset asOf) =>
        (await context.ListOrEmpty<string>(new QueryObject(ContextSql.KnownMaterials, new { t = asOf.UtcTicks })))
        .Select(json => JsonSerializer.Deserialize<MaterialVersion>(json, ContextJson.Options)!).ToArray();
}
