using System.Net;
using System.Text.Json;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using AlfaHackaton.DapperContext.Dapper;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Common;
using ContextService.Contracts.Companies;
using ContextService.Contracts.Materials;
using ContextService.Repositories.Interfaces;
using ContextService.Resources;

namespace ContextService.Repositories;

public sealed class PostgresMaterialRepository(IDapperContext context, CompanyEvidenceRepository evidence) : IMaterialRepository
{
    private sealed record MaterialIdentity(string SourceId, string Kind);
    public async Task<Result<MaterialVersion>> Save(MaterialVersion material)
    {
        using var transaction = context.BeginTransaction();
        await transaction.Command(new QueryObject(ContextSql.WriteLock));
        var existingJson = await transaction.FirstOrDefault<string>(new QueryObject(ContextSql.FindMaterial, new
        {
            material = material.MaterialId,
            version = material.VersionId
        }));
        if (existingJson is not null)
        {
            var existing = JsonSerializer.Deserialize<MaterialVersion>(existingJson, ContextJson.Options)!;
            if (existing.ContentHash != material.ContentHash)
            {
                return new(new Error("This material/version already exists with different content or metadata.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.Conflict });
            }

            transaction.Commit();
            return new(existing, null);
        }

        var identity = await transaction.FirstOrDefault<MaterialIdentity>(new QueryObject(ContextSql.FindMaterialIdentity, new
        {
            material = material.MaterialId
        }));
        if (identity is not null)
        {
            if (identity.SourceId != material.SourceId ||
                identity.Kind != material.Kind)
            {
                return new(new Error("sourceId and kind cannot change within one material identity.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.Conflict });
            }
        }

        if (material.CorrectsVersionId is not null)
        {
            var priorJson = await transaction.FirstOrDefault<string>(new QueryObject(ContextSql.FindMaterial,
                new
                {
                    material = material.MaterialId,
                    version = material.CorrectsVersionId
                }));
            var prior = priorJson is null ? null : JsonSerializer.Deserialize<MaterialVersion>(priorJson, ContextJson.Options);
            if (prior is null)
            {
                return new(new Error("The corrected version must already exist on this material.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.Conflict });
            }

            if (prior.KnownAt > material.KnownAt)
            {
                return new(new Error("A correction cannot be known before its predecessor.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.Conflict });
            }
        }

        await transaction.Command(new QueryObject(ContextSql.InsertMaterial,
            new
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
        {
            await transaction.Command(new QueryObject(ContextSql.InsertMaterialCompany,
                new
                {
                    material = material.MaterialId,
                    version = material.VersionId,
                    company = company.ToString("D")
                }));
        }

        await ContextChanges.Append(transaction, "material:" + material.Kind, material.MaterialId, material.VersionId, material.KnownAt, material.CompanyIds);
        transaction.Commit();
        return new(material, null);
    }

    public async Task<MaterialVersion?> GetAsync(string materialId, string versionId)
    {
        var json = await context.FirstOrDefault<string>(new QueryObject(ContextSql.FindMaterial, new
        {
            material = materialId,
            version = versionId
        }));
        return json is null ? null : JsonSerializer.Deserialize<MaterialVersion>(json, ContextJson.Options);
    }

    public Task<Result<CompanyEvidenceInventory>> CompanyEvidence(Guid companyId, DateTimeOffset asOf) => evidence.CompanyEvidence(companyId, asOf);
    public async Task<IReadOnlyList<MaterialVersion>> GetContextMaterials(Guid companyId, string newsMaterialId, DateTimeOffset asOf) => (await context.ListOrEmpty<string>(new QueryObject(ContextSql.ContextMaterials, new { asof = asOf.UtcTicks, company = companyId.ToString("D"), news = newsMaterialId }))).Select(json => JsonSerializer.Deserialize<MaterialVersion>(json, ContextJson.Options)!).ToArray();
    public async Task<IReadOnlyList<MaterialVersion>> ListKnown(DateTimeOffset asOf) => (await context.ListOrEmpty<string>(new QueryObject(ContextSql.KnownMaterials, new { t = asOf.UtcTicks }))).Select(json => JsonSerializer.Deserialize<MaterialVersion>(json, ContextJson.Options)!).ToArray();
}
