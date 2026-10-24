using System.Net;
using System.Text.Json;
using AlfaHackaton.Result;
using ContextService.Common;
using ContextService.Contracts.Companies;
using ContextService.Contracts.Knowledge;
using ContextService.Contracts.Materials;
using ContextService.Repositories.Interfaces;
using ContextService.Services;
using ContextService.Settings;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class InferenceContractTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-10-24T08:00:00Z");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmbeddingsSendConfiguredModelBearerAndPrefixAndRestoreOriginalIndexes(bool query)
    {
        var config = Config();
        config["Embeddings:ApiKey"] = "synthetic-embedding-key";
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("https://embedding.fixture/v1/embeddings", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("synthetic-embedding-key", request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync());
            Assert.Equal("served-embedding-alias", body.RootElement.GetProperty("model").GetString());
            Assert.Equal(new[] { (query ? "query: " : "passage: ") + "кредит", (query ? "query: " : "passage: ") + "ставка" },
                body.RootElement.GetProperty("input").EnumerateArray().Select(x => x.GetString()));
            return Json(new
            {
                model = "served-embedding-alias",
                data = new[] { new { index = 1, embedding = Vector(2) }, new { index = 0, embedding = Vector(1) } },
                usage = new { prompt_tokens = 2, total_tokens = 2 }
            });
        }));

        var vectors = await Client(http, config).Embed(["кредит", "ставка"], CancellationToken.None, query).RequireContentAsync();

        Assert.Equal(2, vectors.Length);
        Assert.All(vectors, vector => Assert.Equal(384, vector.Length));
        Assert.Equal(1f, vectors[0][0]);
        Assert.Equal(2f, vectors[1][0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmbeddingsWithoutKeyRemainCompatibleWithUnauthenticatedProviders(string? key)
    {
        var config = Config();
        config["Embeddings:ApiKey"] = key;
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(Json(new { data = new[] { new { index = 0, embedding = Vector(1) } } }));
        }));
        await Client(http, config).Embed(["кредит"], CancellationToken.None).RequireContentAsync();
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(" ", "false", false)]
    [InlineData("synthetic-rerank-key", "true", false)]
    [InlineData("synthetic-rerank-key", "true", true)]
    public async Task RerankUsesOwnOptionalBearerRawScoresAndOriginalDocumentIndexes(string? key, string? rawScores, bool dense)
    {
        var config = Config();
        if (!dense)
        {
            config["Embeddings:BaseUrl"] = null;
        }
        config["Embeddings:ApiKey"] = "synthetic-embedding-only-key";
        config["Reranker:ApiKey"] = key;
        config["Reranker:RawScores"] = rawScores;
        var originals = Originals();
        var embeddingCalls = 0;
        var rerankCalls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.Host == "qdrant.fixture")
            {
                Assert.Null(request.Headers.Authorization);
                return Qdrant(request, originals);
            }
            if (request.RequestUri.Host == "embedding.fixture")
            {
                embeddingCalls++;
                Assert.Equal("/v1/embeddings", request.RequestUri.AbsolutePath);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal("synthetic-embedding-only-key", request.Headers.Authorization?.Parameter);
                using var embeddingBody = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync());
                Assert.Equal("served-embedding-alias", embeddingBody.RootElement.GetProperty("model").GetString());
                Assert.Equal("query: кредит", Assert.Single(embeddingBody.RootElement.GetProperty("input").EnumerateArray()).GetString());
                return Json(new { data = new[] { new { index = 0, embedding = Vector(1) } } });
            }
            rerankCalls++;
            Assert.Equal("https://rerank.fixture/v1/rerank", request.RequestUri.ToString());
            Assert.Equal(string.IsNullOrWhiteSpace(key) ? null : "Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(string.IsNullOrWhiteSpace(key) ? null : key, request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync());
            Assert.Equal("served-reranker-alias", body.RootElement.GetProperty("model").GetString());
            Assert.Equal("кредит", body.RootElement.GetProperty("query").GetString());
            Assert.Equal(originals.Select(x => x.Text), body.RootElement.GetProperty("documents").EnumerateArray().Select(x => x.GetString()));
            Assert.Equal(3, body.RootElement.GetProperty("top_n").GetInt32());
            Assert.Equal(rawScores is not null, body.RootElement.TryGetProperty("raw_scores", out var raw));
            if (rawScores is not null)
            {
                Assert.Equal(bool.Parse(rawScores), raw.GetBoolean());
            }
            return Json(new
            {
                model = "served-reranker-alias",
                results = new[] { new { index = 2, relevance_score = 4.25 }, new { index = 0, relevance_score = -0.25 }, new { index = 1, relevance_score = -13.5 } },
                usage = new { prompt_tokens = 3, total_tokens = 3 }
            });
        }));
        using var company = new CompanyKnowledgeFixture();
        var search = new SearchService(Client(http, config), null!, config, new Materials(originals), company.Client);

        var result = await search.Search(new("кредит", Time, Limit: 2, PublicOnly: true), CancellationToken.None).RequireContentAsync();

        Assert.Equal(new[] { "material-2", "material-0" }, result.Hits.Select(x => x.MaterialId));
        Assert.Equal(new[] { 4.25, -0.25 }, result.Hits.Select(x => x.Score));
        Assert.Equal("served-reranker-alias", result.Reranker);
        Assert.Equal(dense ? "dense+lexical-rrf" : "lexical-idf", result.Mode);
        Assert.Equal(dense ? 1 : 0, embeddingCalls);
        Assert.Equal(1, rerankCalls);
        Assert.Equal(3, result.CandidatesExamined);
        Assert.All(result.Hits, hit => Assert.Equal(hit.MaterialId, Assert.Single(hit.SourceRefs).MaterialId));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"results\":{}}")]
    [InlineData("{\"results\":[{\"index\":0,\"relevance_score\":1}]}")]
    [InlineData("{\"results\":[{\"index\":0,\"relevance_score\":1},{\"index\":0,\"relevance_score\":2},{\"index\":2,\"relevance_score\":3}]}")]
    [InlineData("{\"results\":[{\"index\":-1,\"relevance_score\":1}]}")]
    [InlineData("{\"results\":[{\"index\":3,\"relevance_score\":1}]}")]
    [InlineData("{\"results\":[{\"index\":\"0\",\"relevance_score\":1}]}")]
    [InlineData("{\"results\":[{\"index\":0,\"relevance_score\":\"NaN\"}]}")]
    [InlineData("{\"results\":[{\"index\":0,\"relevance_score\":1e999}]}")]
    public async Task InvalidRerankResponsesReturnExternalError(string response)
    {
        var config = Config();
        config["Embeddings:BaseUrl"] = null;
        var originals = Originals();
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.RequestUri!.Host == "qdrant.fixture"
            ? Qdrant(request, originals) : Payload(response))));
        using var company = new CompanyKnowledgeFixture();
        var search = new SearchService(Client(http, config), null!, config, new Materials(originals), company.Client);

        var result = await search.Search(new("кредит", Time, PublicOnly: true), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadGateway, Assert.IsType<AlfaHackaton.Result.Errors.Error>(result.Error).HttpStatusCode);
    }

    [Theory]
    [InlineData("not json", 1)]
    [InlineData("{}", 1)]
    [InlineData("{\"data\":{}}", 1)]
    [InlineData("{\"data\":[{\"index\":0,\"embedding\":[1,0]}]}", 2)]
    [InlineData("{\"data\":[{\"index\":0,\"embedding\":[1,0]},{\"index\":0,\"embedding\":[0,1]}]}", 2)]
    [InlineData("{\"data\":[{\"index\":2,\"embedding\":[1,0]}]}", 1)]
    [InlineData("{\"data\":[{\"index\":\"0\",\"embedding\":[1,0]}]}", 1)]
    [InlineData("{\"data\":[{\"index\":0,\"embedding\":[1]}]}", 1)]
    [InlineData("{\"data\":[{\"index\":0,\"embedding\":\"base64\"}]}", 1)]
    [InlineData("{\"data\":[{\"index\":0,\"embedding\":[1e999,0]}]}", 1)]
    public async Task InvalidEmbeddingResponsesReturnExternalError(string response, int count)
    {
        var config = Config();
        config["Embeddings:Dimensions"] = "2";
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Payload(response))));

        var result = await Client(http, config).Embed(Enumerable.Repeat("synthetic", count).ToArray(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadGateway, Assert.IsType<AlfaHackaton.Result.Errors.Error>(result.Error).HttpStatusCode);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task UpstreamFailuresDoNotBecomeEmptySuccessOrEchoResponseBodies(int status)
    {
        var config = Config();
        config["Embeddings:ApiKey"] = "synthetic-private-key";
        config["Reranker:ApiKey"] = "synthetic-private-key";
        var originals = Originals();
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.RequestUri!.Host == "qdrant.fixture"
            ? Qdrant(request, originals) : new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("synthetic-private-key") })));
        var client = Client(http, config);
        var embedded = await client.Embed(["кредит"], CancellationToken.None);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, embedded.Error!.HttpStatusCode);
        Assert.DoesNotContain("synthetic-private-key", embedded.Error.Title);
        config["Embeddings:BaseUrl"] = null;
        using var company = new CompanyKnowledgeFixture();
        var search = new SearchService(client, null!, config, new Materials(originals), company.Client);

        var ranked = await search.Search(new("кредит", Time, PublicOnly: true), CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ranked.Error!.HttpStatusCode);
        Assert.DoesNotContain("synthetic-private-key", ranked.Error.Title);
    }

    private static ConfigurationManager Config() => new()
    {
        ["Qdrant:Collection"] = "fixture",
        ["Embeddings:BaseUrl"] = "https://embedding.fixture/v1/",
        ["Embeddings:Model"] = "served-embedding-alias",
        ["Embeddings:Dimensions"] = "384",
        ["Embeddings:QueryPrefix"] = "query: ",
        ["Embeddings:PassagePrefix"] = "passage: ",
        ["Reranker:BaseUrl"] = "https://rerank.fixture/v1/",
        ["Reranker:Model"] = "served-reranker-alias"
    };

    private static float[] Vector(float first) => new[] { first }.Concat(new float[383]).ToArray();
    private static MaterialVersion[] Originals() => Enumerable.Range(0, 3)
        .Select(i => new MaterialVersion($"material-{i}", "v1", "synthetic", "market", "Synthetic", $"Документ {i}", Time, Time, null, [], [], $"hash-{i}")).ToArray();
    private static HttpResponseMessage Json(object value) => Payload(JsonSerializer.Serialize(value, ContextJson.Options));
    private static HttpResponseMessage Payload(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    private static HttpResponseMessage Qdrant(HttpRequestMessage request, MaterialVersion[] originals) => request.Method == HttpMethod.Get
        ? Json(new { status = "ok" })
        : Json(new { result = new { points = originals.Select((m, i) => new { score = 9 - i, payload = new { kind = m.Kind, companyIds = m.CompanyIds, materialId = m.MaterialId, versionId = m.VersionId, hash = m.ContentHash, text = m.Text, sourceRefs = new[] { new SourceReference(m.MaterialId, m.VersionId, m.ContentHash) } } }) } });
    private static RetrievalClient Client(HttpClient http, IConfiguration config)
    {
        var qdrant = new QdrantHttpSettings { BaseAddress = "http://qdrant.fixture/" };
        var inference = new InferenceHttpSettings();
        http.BaseAddress = new Uri(qdrant.BaseAddress);
        return new(new RetrievalTransport<QdrantHttpSettings>(new AlfaHackaton.HttpClient.Http.HttpClient<QdrantHttpSettings>(http, qdrant), qdrant),
            new RetrievalTransport<InferenceHttpSettings>(new AlfaHackaton.HttpClient.Http.HttpClient<InferenceHttpSettings>(http, inference), inference), config);
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class Materials(MaterialVersion[] originals) : IMaterialRepository
    {
        public Task<MaterialVersion?> GetAsync(string materialId, string versionId) => Task.FromResult(originals.SingleOrDefault(m => m.MaterialId == materialId && m.VersionId == versionId));
        public Task<IReadOnlyList<MaterialVersion>> ListKnown(DateTimeOffset asOf) => Task.FromResult<IReadOnlyList<MaterialVersion>>(originals.Where(m => m.KnownAt <= asOf).ToArray());
        public Task<Result<MaterialVersion>> Save(MaterialVersion material) => throw new NotSupportedException();
        public Task<Result<CompanyEvidenceInventory>> CompanyEvidence(Guid companyId, DateTimeOffset asOf) => throw new NotSupportedException();
        public Task<IReadOnlyList<MaterialVersion>> GetContextMaterials(Guid companyId, string newsMaterialId, DateTimeOffset asOf) => throw new NotSupportedException();
    }
}
