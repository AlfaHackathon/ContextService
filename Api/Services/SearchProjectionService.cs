using System.Text.Json;
using ContextService.Common;
using ContextService.Contracts.Search;
using ContextService.Models;
using ContextService.Repositories;
using ContextService.Repositories.Interfaces;

namespace ContextService.Services;

public sealed class SearchProjectionService : IDisposable
{
    private readonly RetrievalClient client;
    private readonly IConfiguration config;
    private readonly IMaterialRepository materials;
    private readonly KnowledgeService knowledge;
    private readonly SearchProjectionRepository projection;
    private readonly SemaphoreSlim syncLock = new(1);
    public SearchProjectionService(RetrievalClient client, SearchProjectionRepository projection, IConfiguration config,
        IMaterialRepository materials, KnowledgeService knowledge)
    {
        this.client = client;
        this.config = config;
        this.materials = materials;
        this.knowledge = knowledge;
        this.projection = projection;
    }
    private string Pipeline => $"{client.Qdrant.BaseAddress}|{client.Collection}|sparse-v1|{config["Embeddings:Model"]}|{config["Embeddings:Dimensions"]}|{client.Dense}";
    public async Task<SearchProjectionResult> Sync(CancellationToken ct)
    {
        await syncLock.WaitAsync(ct);
        try
        {
            await client.Ensure(ct);
            // Canonical sources are scanned on restart; only missing/changed projection entries are sent.
            // A failed upsert never receives a durable mark and is retried by the worker.
            var entries = new List<IndexEntry>();
            foreach (var p in (await knowledge.CompanyVersions()))
            {
                entries.Add(new IndexEntry(RetrievalEncoding.Stable("profile", p.CompanyId.ToString(), p.RevisionId),
                    string.Join(' ', new[] { p.Name, p.Description }.Concat(p.IndustryCodes).Concat(p.Exposures)),
                    p.KnownAt, [p.CompanyId], "profile", null, p.RevisionId,
                    RetrievalEncoding.Hash(JsonSerializer.Serialize(p, ContextJson.Options)), p.SourceRefs));
            }

            foreach (var m in (await materials.ListKnown(DateTimeOffset.MaxValue)).Where(x => x.Kind != "news"))
            {
                for (var start = 0; start < m.Text.Length; start += 1500)
                {
                    var text = m.Text.Substring(start, Math.Min(1800, m.Text.Length - start));
                    entries.Add(new IndexEntry(RetrievalEncoding.Stable("chunk", m.MaterialId, m.VersionId, start.ToString(System.Globalization.CultureInfo.InvariantCulture)), m.Title + "\n" + text,
                        m.KnownAt, m.CompanyIds, m.Kind, m.MaterialId, m.VersionId, m.ContentHash,
                        [new(m.MaterialId, m.VersionId, m.ContentHash)]));
                }
            }
            var marks = await projection.Read(Pipeline);
            var changed = entries.Where(x => !marks.TryGetValue(x.Id, out var h) || h != x.Hash).ToArray();
            foreach (var batch in changed.Chunk(32))
            {
                var dense = client.Dense ? await client.Embed(batch.Select(x => x.Text).ToArray(), ct) : null;
                var points = batch.Select((e, i) =>
                {
                    var vector = new Dictionary<string, object> { ["lexical"] = RetrievalEncoding.Sparse(e.Text) };
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
                RetrievalClient.RequireSuccess(response);
                await projection.Mark(Pipeline, batch.Select(e => new KeyValuePair<string, string>(e.Id, e.Hash)));
            }
            return new SearchProjectionResult(changed.Length, entries.Count, client.Dense ? "dense+lexical-rrf" : "lexical-idf", client.Collection);
        }
        finally { syncLock.Release(); }
    }

    public void Dispose() => syncLock.Dispose();
}
