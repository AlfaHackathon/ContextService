using System.Text.Json;
using AlfaHackaton.Result;
using ContextService.Common;
using ContextService.Contracts.Search;
using ContextService.Models;
using ContextService.Repositories;
using ContextService.Repositories.Interfaces;

namespace ContextService.Services;

public sealed class SearchProjectionService : IDisposable
{
    private static readonly string[] HashPayload = ["hash"];
    private readonly RetrievalClient client;
    private readonly IConfiguration config;
    private readonly IMaterialRepository materials;
    private readonly CompanyKnowledgeClient knowledge;
    private readonly SearchProjectionRepository projection;
    private readonly SemaphoreSlim syncLock = new(1);
    public SearchProjectionService(RetrievalClient client, SearchProjectionRepository projection, IConfiguration config, IMaterialRepository materials, CompanyKnowledgeClient knowledge)
    {
        this.client = client;
        this.config = config;
        this.materials = materials;
        this.knowledge = knowledge;
        this.projection = projection;
    }

    private string Pipeline => $"{client.Qdrant.BaseAddress}|{client.Collection}|sparse-v1|{config["Embeddings:Model"]}|{config["Embeddings:Dimensions"]}|{client.Dense}";

    public async Task<Result<SearchProjectionResult>> Sync(CancellationToken ct)
    {
        await syncLock.WaitAsync(ct);
        try
        {
            if ((await client.Ensure(ct)).Error is { } initializationError)
            {
                return new(initializationError);
            }
            // Durable marks alone cannot prove the Qdrant collection still contains its points.
            // Reconcile the actual projection before skipping entries; failed upserts remain retryable.
            var entries = new List<IndexEntry>();
            var profiles = await knowledge.CompanyVersions(ct);
            if (profiles.Error is { } companyError)
            {
                return new(companyError);
            }
            foreach (var p in profiles.Content!)
            {
                entries.Add(new IndexEntry(RetrievalEncoding.Stable("profile", p.CompanyId.ToString(), p.RevisionId),
                    string.Join(' ', new[] { p.Name, p.Description }.Concat(p.IndustryCodes).Concat(p.Exposures)),
                    p.KnownAt,
                    [p.CompanyId],
                    "profile",
                    null,
                    p.RevisionId,
                    RetrievalEncoding.Hash(JsonSerializer.Serialize(p, ContextJson.Options)),
                    p.SourceRefs));
            }

            var bankProfiles = await knowledge.BankProfileVersions(ct);
            if (bankProfiles.Error is { } bankError)
            {
                return new(bankError);
            }
            foreach (var bank in bankProfiles.Content!)
            {
                entries.Add(new IndexEntry(RetrievalEncoding.Stable("bank-profile", bank.CompanyId.ToString("D"), bank.RevisionId),
                    CompanyBankProfileSearchText.Build(bank), bank.KnownAt, [bank.CompanyId], "bank_profile", null,
                    bank.RevisionId, RetrievalEncoding.Hash(JsonSerializer.Serialize(bank, ContextJson.Options)), bank.SourceRefs));
            }

            foreach (var m in (await materials.ListKnown(DateTimeOffset.MaxValue)).Where(x => x.Kind != "news"))
            {
                for (var start = 0; start < m.Text.Length; start += 1500)
                {
                    var text = m.Text.Substring(start, Math.Min(1800, m.Text.Length - start));
                    entries.Add(new IndexEntry(RetrievalEncoding.Stable("chunk", m.MaterialId, m.VersionId, start.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        m.Title + "\n" + text,
                        m.KnownAt,
                        m.CompanyIds,
                        m.Kind,
                        m.MaterialId,
                        m.VersionId,
                        m.ContentHash,
                        [new(m.MaterialId, m.VersionId, m.ContentHash)]));
                }
            }

            var marks = await projection.Read(Pipeline);
            var changed = new List<IndexEntry>();
            foreach (var batch in entries.Chunk(256))
            {
                using var response = await client.Qdrant.PostAsJsonAsync($"collections/{client.Collection}/points", new
                {
                    ids = batch.Select(x => x.Id).ToArray(),
                    with_payload = HashPayload,
                    with_vector = false
                }, ct);
                if (RetrievalClient.RequireSuccess(response).Error is { } reconciliationError)
                {
                    return new(reconciliationError);
                }
                using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var indexed = json.RootElement.GetProperty("result").EnumerateArray().ToDictionary(
                    x => x.GetProperty("id").GetString()!,
                    x => x.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object &&
                        payload.TryGetProperty("hash", out var hash) && hash.ValueKind == JsonValueKind.String ? hash.GetString() : null);
                changed.AddRange(batch.Where(x => !marks.TryGetValue(x.Id, out var mark) || mark != x.Hash ||
                    !indexed.TryGetValue(x.Id, out var hash) || hash != x.Hash));
            }
            foreach (var batch in changed.Chunk(32))
            {
                float[][]? dense = null;
                if (client.Dense)
                {
                    var embeddings = await client.Embed(batch.Select(x => x.Text).ToArray(), ct);
                    if (embeddings.Error is { } embeddingError)
                    {
                        return new(embeddingError);
                    }
                    dense = embeddings.Content;
                }

                var points = batch.Select((e, i) =>
                {
                    var vector = new Dictionary<string, object>
                    {
                        ["lexical"] = RetrievalEncoding.Sparse(e.Text)
                    };
                    if (dense is not null)
                    {
                        vector["dense"] = dense[i];
                    }

                    return new
                    {
                        id = e.Id,
                        vector,
                        payload = new
                        {
                            text = e.Text,
                            knownAt = e.KnownAt,
                            companyIds = e.CompanyIds.Select(x => x.ToString("D")).ToArray(),
                            kind = e.Kind,
                            materialId = e.MaterialId,
                            versionId = e.VersionId,
                            hash = e.Hash,
                            sourceRefs = e.SourceRefs
                        }
                    };
                }).ToArray();
                using var response = await client.Qdrant.PutAsJsonAsync($"collections/{client.Collection}/points?wait=true", new
                {
                    points
                }, ct);
                if (RetrievalClient.RequireSuccess(response).Error is { } responseError)
                {
                    return new(responseError);
                }
                await projection.Mark(Pipeline, batch.Select(e => new KeyValuePair<string, string>(e.Id, e.Hash)));
            }

            return new(new SearchProjectionResult(changed.Count, entries.Count, client.Dense ? "dense+lexical-rrf" : "lexical-idf", client.Collection), null);
        }
        finally
        {
            syncLock.Release();
        }
    }

    public void Dispose() => syncLock.Dispose();
}
