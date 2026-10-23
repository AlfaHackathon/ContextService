using System.Net;
using System.Text.Json;
using AlfaHackaton.Result;
using AlfaHackaton.Result.Errors;
using ContextService.Common;
using ContextService.Contracts.Knowledge;
using ContextService.Contracts.Materials;
using ContextService.Contracts.Search;
using ContextService.Repositories.Interfaces;

namespace ContextService.Services;

public sealed class SearchService
{
    private readonly RetrievalClient client;
    private readonly SearchProjectionService projection;
    private readonly IMaterialRepository materials;
    private readonly KnowledgeService knowledge;
    private readonly IConfiguration config;
    public SearchService(RetrievalClient client, SearchProjectionService projection, IConfiguration config, IMaterialRepository materials, KnowledgeService knowledge)
    {
        this.config = config;
        this.materials = materials;
        this.knowledge = knowledge;
        this.client = client;
        this.projection = projection;
    }

    public Task<Result<SearchProjectionResult>> Sync(CancellationToken ct) => projection.Sync(ct);
    public static object Sparse(string text) => RetrievalEncoding.Sparse(text);
    public async Task<Result<SearchResponse>> Search(SearchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query) ||
            request.Query.Length > 16000 ||
            request.AsOf == default ||
            request.Limit is < 1 or > 64)
        {
            return new(new Error("A bounded query, asOf and limit 1–64 are required.", ErrorSource.Client, ErrorType.CorruptedInfo) { HttpStatusCode = HttpStatusCode.BadRequest });
        }

        if ((await client.Ensure(ct)).Error is { } initializationError)
        {
            return new(initializationError);
        }
        var must = new List<object>
        {
            new
            {
                key = "knownAt",
                range = new
                {
                    lte = request.AsOf.ToUniversalTime().ToString("O")
                }
            }
        };
        if (request.CompanyId.HasValue)
        {
            must.Add(new
            {
                should = new object[] { new { key = "companyIds", match = new { value = request.CompanyId.Value.ToString("D") } }, new { must = new object[] { new { key = "kind", match = new { value = "market" } }, new { is_empty = new { key = "companyIds" } } } } }
            });
        }
        else if (request.PublicOnly)
        {
            must.Add(new
            {
                key = "kind",
                match = new
                {
                    value = "market"
                }
            });
            must.Add(new
            {
                is_empty = new
                {
                    key = "companyIds"
                }
            });
        }
        else
        {
            must.Add(new
            {
                key = "kind",
                match = new
                {
                    value = "profile"
                }
            });
        }

        var filter = new
        {
            must
        };
        var count = Math.Min(256, Math.Max(request.Limit * 4, 48));
        var branches = new List<object>
        {
            new
            {
                query = RetrievalEncoding.Sparse(request.Query),
                @using = "lexical",
                filter,
                limit = count
            }
        };
        if (client.Dense)
        {
            var embeddings = await client.Embed([request.Query], ct, true);
            if (embeddings.Error is { } embeddingsError)
            {
                return new(embeddingsError);
            }

            branches.Add(new
            {
                query = (embeddings.Content!)[0],
                @using = "dense",
                filter,
                limit = count
            });
        }

        object body = client.Dense ? new
        {
            prefetch = branches,
            query = new
            {
                fusion = "rrf"
            },
            limit = count,
            with_payload = true
        }

        : new
        {
            query = RetrievalEncoding.Sparse(request.Query),
            @using = "lexical",
            filter,
            limit = count,
            with_payload = true
        };
        using var response = await client.Qdrant.PostAsJsonAsync($"collections/{client.Collection}/points/query", body, ct);
        if (RetrievalClient.RequireSuccess(response).Error is { } responseError)
        {
            return new(responseError);
        }
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var hits = new List<SearchHit>();
        var texts = new List<string>();
        var profiles = (await knowledge.Companies(request.AsOf)).ToDictionary(x => x.CompanyId);
        var knownVersions = (request.PublicOnly ||
                request.CompanyId.HasValue) ? (await materials.ListKnown(request.AsOf)).GroupBy(m => m.MaterialId).ToDictionary(g => g.Key, g => g.Where(m => m.KnownAt == g.Max(x => x.KnownAt)).ToArray()) : new Dictionary<string, MaterialVersion[]>();
        foreach (var row in json.RootElement.GetProperty("result").GetProperty("points").EnumerateArray())
        {
            var p = row.GetProperty("payload");
            var kind = p.GetProperty("kind").GetString()!;
            var refs = p.GetProperty("sourceRefs").Deserialize<SourceReference[]>(ContextJson.Options)!;
            var ids = p.GetProperty("companyIds").EnumerateArray().Select(x => Guid.Parse(x.GetString()!)).ToArray();
            if (kind == "profile")
            {
                foreach (var id in ids)
                {
                    if (profiles.TryGetValue(id, out var profile) &&
                        p.GetProperty("versionId").GetString() == profile.RevisionId)
                    {
                        hits.Add(new(id, profile.SourceRefs, row.GetProperty("score").GetDouble(), null, kind, null, profile.RevisionId));
                        texts.Add(p.GetProperty("text").GetString()!);
                    }
                }
            }
            else
            {
                var m = await materials.GetAsync(p.GetProperty("materialId").GetString()!, p.GetProperty("versionId").GetString()!);
                if (m is null ||
                    m.ContentHash != p.GetProperty("hash").GetString() ||
                    m.KnownAt > request.AsOf ||
                    (!request.CompanyId.HasValue &&
                        !(request.PublicOnly &&
                            m.Kind == "market" &&
                            m.CompanyIds.Length == 0)) ||
                    (request.CompanyId.HasValue &&
                        m.CompanyIds.Length > 0 &&
                        !m.CompanyIds.Contains(request.CompanyId.Value)))
                {
                    continue;
                }

                var excerpt = p.GetProperty("text").GetString()!;
                var related = knownVersions.GetValueOrDefault(m.MaterialId, []).Where(x => x.VersionId != m.VersionId &&
                        (request.CompanyId.HasValue ? x.CompanyIds.Length == 0 ||
                            x.CompanyIds.Contains(request.CompanyId.Value) : x.Kind == "market" &&
                            x.CompanyIds.Length == 0)).Select(x => new SourceReference(x.MaterialId, x.VersionId, x.ContentHash)).ToArray();
                hits.Add(new(request.CompanyId ?? Guid.Empty, refs, row.GetProperty("score").GetDouble(), excerpt, kind, m.MaterialId, m.VersionId, related));
                texts.Add(excerpt);
            }
        }

        var reranker = "none";
        if (hits.Count > 0 &&
            !string.IsNullOrWhiteSpace(config["Reranker:BaseUrl"]))
        {
            using var reranked = await client.Inference.PostAsJsonAsync(RetrievalClient.SafeUri(config["Reranker:BaseUrl"]!.TrimEnd('/') + "/rerank"),
                new
                {
                    model = config["Reranker:Model"],
                    query = request.Query,
                    documents = texts,
                    top_n = hits.Count
                },
                ct);
            if (RetrievalClient.RequireSuccess(reranked).Error is { } responseError2)
            {
                return new(responseError2);
            }
            using var data = await JsonDocument.ParseAsync(await reranked.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var scores = new Dictionary<int, double>();
            foreach (var item in data.RootElement.GetProperty("results").EnumerateArray())
            {
                var index = item.GetProperty("index").GetInt32();
                var score = item.GetProperty("relevance_score").GetDouble();
                if (index < 0 ||
                    index >= hits.Count ||
                    !double.IsFinite(score) ||
                    !scores.TryAdd(index, score))
                {
                    return new(new Error("Reranker returned invalid document indices or scores.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.BadGateway });
                }
            }

            if (scores.Count != hits.Count)
            {
                return new(new Error("Reranker omitted candidate documents.", ErrorSource.External, ErrorType.Undefined) { HttpStatusCode = HttpStatusCode.BadGateway });
            }

            hits = hits.Select((h, i) => h with { Score = scores[i] }).ToList();
            reranker = config["Reranker:Model"] ?? "configured";
        }

        return new(new SearchResponse(hits.OrderByDescending(x => x.Score).Take(request.Limit).ToArray(),
            client.Dense ? "dense+lexical-rrf" : "lexical-idf",
            reranker,
            false,
            hits.Count,
            request.AsOf),
            null);
    }
}
