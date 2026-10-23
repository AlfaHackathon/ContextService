using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AlfaHackaton.Result;
using ContextService.Models;
using ContextService.Settings;

namespace ContextService.Services;

public sealed class RetrievalClient
{
    private readonly IConfiguration config;
    private readonly RetrievalTransport<QdrantHttpSettings> qdrant;
    private readonly RetrievalTransport<InferenceHttpSettings> inference;
    private readonly string collection;
    private bool initialized;
    public RetrievalTransport<QdrantHttpSettings> Qdrant => qdrant;
    public RetrievalTransport<InferenceHttpSettings> Inference => inference;
    public string Collection => collection;
    public bool Dense => !string.IsNullOrWhiteSpace(config["Embeddings:BaseUrl"]);

    public RetrievalClient(RetrievalTransport<QdrantHttpSettings> qdrant, RetrievalTransport<InferenceHttpSettings> inference, IConfiguration config)
    {
        this.config = config;
        this.qdrant = qdrant;
        this.inference = inference;
        collection = config["Qdrant:Collection"] ?? "alfa_context_v1";
        if (!Regex.IsMatch(collection, "^[A-Za-z0-9_-]{1,100}$"))
        {
            throw new InvalidOperationException("Invalid Qdrant collection name.");
        }
    }

    public async Task<Result> Ensure(CancellationToken ct)
    {
        if (initialized)
        {
            return new();
        }

        using var check = await qdrant.GetAsync($"collections/{collection}", ct);
        if (check.StatusCode == HttpStatusCode.NotFound)
        {
            var dimensions = config.GetValue<int?>("Embeddings:Dimensions") ?? 384;
            object vectors = Dense ? new Dictionary<string, object>
            {
                ["dense"] = new
                {
                    size = dimensions,
                    distance = "Cosine"
                }
            }

            : new
            {
            };
            using var created = await qdrant.PutAsJsonAsync($"collections/{collection}",
                new
                {
                    vectors,
                    sparse_vectors = new
                    {
                        lexical = new
                        {
                            modifier = "idf"
                        }
                    }
                },
                ct);
            if (RequireSuccess(created).Error is { } responseError)
            {
                return new(responseError);
            }
            foreach (var key in new[]
            {
                "kind",
                "companyIds"
            }

            )
            {
                using var indexed = await qdrant.PutAsJsonAsync($"collections/{collection}/index?wait=true",
                    new
                    {
                        field_name = key,
                        field_schema = "keyword"
                    },
                    ct);
                if (RequireSuccess(indexed).Error is { } responseError2)
                {
                    return new(responseError2);
                }
            }
        }
        else
        {
            if (RequireSuccess(check).Error is { } responseError3)
            {
                return new(responseError3);
            }
        }

        initialized = true;
        return new();
    }

    public async Task<Result<float[][]>> Embed(string[] texts, CancellationToken ct, bool query = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SafeUri(config["Embeddings:BaseUrl"]!.TrimEnd('/') + "/embeddings"));
        if (!string.IsNullOrWhiteSpace(config["Embeddings:ApiKey"]))
        {
            request.Headers.Authorization = new("Bearer", config["Embeddings:ApiKey"]);
        }

        var prefix = config[query ? "Embeddings:QueryPrefix" : "Embeddings:PassagePrefix"] ?? "";
        request.Content = JsonContent.Create(new
        {
            model = config["Embeddings:Model"],
            input = texts.Select(t => prefix + t).ToArray()
        });
        using var response = await inference.SendAsync(request, ct);
        if (RequireSuccess(response).Error is { } responseError4)
        {
            return new(responseError4);
        }
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var rows = json.RootElement.GetProperty("data").EnumerateArray().OrderBy(x => x.GetProperty("index").GetInt32()).ToArray();
        var values = rows.Select(x => x.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray()).ToArray();
        var dimensions = config.GetValue<int?>("Embeddings:Dimensions") ?? 384;
        if (values.Length != texts.Length ||
            rows.Where((x, i) => x.GetProperty("index").GetInt32() != i).Any() ||
            values.Any(x => x.Length != dimensions ||
                x.Any(v => !float.IsFinite(v))))
        {
            return new(new ApiError(502, "invalid_embeddings", "Embedding count, indices, dimensions or values do not match the configured model."));
        }

        return new(values, null);
    }

    public static Uri SafeUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length > 0 ||
            uri.Query.Length > 0 ||
            uri.Fragment.Length > 0)
        {
            throw new InvalidOperationException("Retrieval endpoints require HTTP(S) URLs without embedded credentials, query or fragment.");
        }

        return uri;
    }

    public static Result RequireSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            return new(new ApiError(503, "retrieval_upstream_unavailable", $"Retrieval upstream returned HTTP {(int)response.StatusCode}; no empty success is implied."));
        }

        return new();
    }
}
