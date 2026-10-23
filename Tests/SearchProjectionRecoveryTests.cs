using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ContextService.Repositories;
using ContextService.Services;
using ContextService.Settings;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

public sealed class SearchProjectionRecoveryTests : PostgresTestBase
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CollectionLossRestoresMaterialsDespiteDurableMarks(bool restart, bool alreadyRecreated)
    {
        var repository = Repository();
        var materials = new MaterialService(repository, 120000);
        var time = DateTimeOffset.Parse("2026-10-24T08:00:00Z");
        var original = await materials.Save(new("terms", "1", "fixture", "market", "Terms", "Original terms", time, time, null, [], [])).RequireContentAsync();
        var knowledge = new KnowledgeService(new KnowledgeStore(Database), materials);
        using var handler = new QdrantHandler();
        using var http = new HttpClient(handler, false);
        using var projection = CreateProjection(http, repository, knowledge);
        Assert.Equal(1, (await projection.Sync(CancellationToken.None).RequireContentAsync()).Indexed);
        var initial = Assert.Single(handler.Points).Value.GetProperty("payload").Clone();
        handler.DropCollection();
        if (alreadyRecreated)
        {
            handler.RecreateEmptyCollection();
        }

        using var restarted = CreateProjection(http, repository, knowledge);
        var restored = await (restart ? restarted : projection).Sync(CancellationToken.None).RequireContentAsync();

        Assert.Equal(1, restored.Indexed);
        var payload = Assert.Single(handler.Points).Value.GetProperty("payload");
        Assert.Equal(original.MaterialId, payload.GetProperty("materialId").GetString());
        Assert.Equal(original.ContentHash, payload.GetProperty("hash").GetString());
        Assert.Equal(initial.GetRawText(), payload.GetRawText());
        Assert.Equal(2, handler.CreatedCollections);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconciliationRepairsMissingOrStalePointsWithoutReembeddingIntactEntries(bool numericHash)
    {
        var repository = Repository();
        var materials = new MaterialService(repository, 120000);
        var time = DateTimeOffset.Parse("2026-10-24T08:00:00Z");
        var hashes = new Dictionary<string, string>();
        for (var i = 0; i < 3; i++)
        {
            var original = await materials.Save(new($"long-terms-{i}", "1", "fixture", "market", "Terms", new string('x', 1500 * 90), time, time, null, [], [])).RequireContentAsync();
            hashes.Add(original.MaterialId, original.ContentHash);
        }
        var knowledge = new KnowledgeService(new KnowledgeStore(Database), materials);
        using var handler = new QdrantHandler();
        using var http = new HttpClient(handler, false);
        using var projection = CreateProjection(http, repository, knowledge, dense: true);
        Assert.Equal(270, (await projection.Sync(CancellationToken.None).RequireContentAsync()).Indexed);
        Assert.Equal(270, handler.EmbeddedTexts);
        Assert.Equal(0, (await projection.Sync(CancellationToken.None).RequireContentAsync()).Indexed);
        Assert.Equal(270, handler.EmbeddedTexts);
        var ids = handler.Points.Keys.ToArray();
        handler.Points.Remove(ids[0]);
        var stale = JsonNode.Parse(handler.Points[ids[1]].GetRawText())!;
        stale["payload"]!["hash"] = numericHash ? JsonValue.Create(123) : JsonValue.Create("stale-hash");
        handler.Points[ids[1]] = JsonSerializer.SerializeToElement(stale);
        var missingHash = JsonNode.Parse(handler.Points[ids[^1]].GetRawText())!;
        missingHash["payload"] = null;
        handler.Points[ids[^1]] = JsonSerializer.SerializeToElement(missingHash);

        var repaired = await projection.Sync(CancellationToken.None).RequireContentAsync();

        Assert.Equal(3, repaired.Indexed);
        Assert.Equal(270, handler.Points.Count);
        Assert.Equal(273, handler.EmbeddedTexts);
        Assert.All(handler.Points.Values, point =>
        {
            var payload = point.GetProperty("payload");
            Assert.Equal(hashes[payload.GetProperty("materialId").GetString()!], payload.GetProperty("hash").GetString());
        });
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task UpstreamFailureReturnsErrorAndRetryRebuildsTheProjection(string failure)
    {
        var repository = Repository();
        var materials = new MaterialService(repository, 120000);
        var time = DateTimeOffset.Parse("2026-10-24T08:00:00Z");
        await materials.Save(new("terms", "1", "fixture", "market", "Terms", "Original terms", time, time, null, [], [])).RequireContentAsync();
        var knowledge = new KnowledgeService(new KnowledgeStore(Database), materials);
        using var handler = new QdrantHandler { Failure = failure };
        using var http = new HttpClient(handler, false);
        using var projection = CreateProjection(http, repository, knowledge);

        var failed = await projection.Sync(CancellationToken.None);

        Assert.NotNull(failed.Error);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.Error.HttpStatusCode);
        Assert.Empty(handler.Points);
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var marks = new NpgsqlCommand("SELECT COUNT(*) FROM search_projection", connection);
            Assert.Equal(0L, await marks.ExecuteScalarAsync());
        }
        handler.Failure = null;
        Assert.Equal(1, (await projection.Sync(CancellationToken.None).RequireContentAsync()).Indexed);
        Assert.Single(handler.Points);
        Assert.Equal(0, (await projection.Sync(CancellationToken.None).RequireContentAsync()).Indexed);
    }

    private SearchProjectionService CreateProjection(HttpClient http, PostgresMaterialRepository repository, KnowledgeService knowledge, bool dense = false)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Qdrant:Collection"] = "fixture",
            ["Embeddings:BaseUrl"] = dense ? "http://inference.fixture" : null,
            ["Embeddings:Dimensions"] = dense ? "2" : null
        }).Build();
        var qdrantSettings = new QdrantHttpSettings { BaseAddress = "http://qdrant.fixture/" };
        var inferenceSettings = new InferenceHttpSettings();
        http.BaseAddress ??= new Uri(qdrantSettings.BaseAddress);
        var client = new RetrievalClient(
            new RetrievalTransport<QdrantHttpSettings>(new AlfaHackaton.HttpClient.Http.HttpClient<QdrantHttpSettings>(http, qdrantSettings), qdrantSettings),
            new RetrievalTransport<InferenceHttpSettings>(new AlfaHackaton.HttpClient.Http.HttpClient<InferenceHttpSettings>(http, inferenceSettings), inferenceSettings), config);
        return new SearchProjectionService(client, new SearchProjectionRepository(Database), config, repository, knowledge);
    }

    private sealed class QdrantHandler : HttpMessageHandler
    {
        private bool exists;
        public Dictionary<string, JsonElement> Points { get; } = [];
        public int CreatedCollections
        {
            get; private set;
        }
        public int EmbeddedTexts
        {
            get; private set;
        }
        public string? Failure
        {
            get; set;
        }
        public void DropCollection()
        {
            exists = false;
            Points.Clear();
        }
        public void RecreateEmptyCollection()
        {
            exists = true;
            CreatedCollections++;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/embeddings")
            {
                using var input = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
                var count = input.RootElement.GetProperty("input").GetArrayLength();
                Assert.InRange(count, 1, 32);
                EmbeddedTexts += count;
                return Response(HttpStatusCode.OK, new
                {
                    data = Enumerable.Range(0, count).Select(index => new { index, embedding = new[] { 1f, 0f } })
                });
            }
            if (request.Method == HttpMethod.Put && path == "/collections/fixture")
            {
                exists = true;
                CreatedCollections++;
                return Response(HttpStatusCode.OK, new
                {
                    result = true
                });
            }
            if (!exists)
            {
                return Response(HttpStatusCode.NotFound, new
                {
                    status = "collection missing"
                });
            }
            if (request.Method == HttpMethod.Get || path == "/collections/fixture/index")
            {
                return Response(HttpStatusCode.OK, new
                {
                    result = new
                    {
                    }
                });
            }
            Assert.Equal("/collections/fixture/points", path);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            if (request.Method == HttpMethod.Put)
            {
                if (Failure == "write")
                {
                    return Response(HttpStatusCode.ServiceUnavailable, new
                    {
                        status = "synthetic write failure"
                    });
                }
                Assert.InRange(body.RootElement.GetProperty("points").GetArrayLength(), 1, 32);
                foreach (var point in body.RootElement.GetProperty("points").EnumerateArray())
                {
                    Points[point.GetProperty("id").GetString()!] = point.Clone();
                }
                return Response(HttpStatusCode.OK, new
                {
                    result = new
                    {
                        status = "completed"
                    }
                });
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            if (Failure == "read")
            {
                return Response(HttpStatusCode.ServiceUnavailable, new
                {
                    status = "synthetic read failure"
                });
            }
            Assert.False(body.RootElement.GetProperty("with_vector").GetBoolean());
            Assert.Equal(new[] { "hash" }, body.RootElement.GetProperty("with_payload").EnumerateArray().Select(x => x.GetString()));
            var ids = body.RootElement.GetProperty("ids").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.InRange(ids.Length, 1, 256);
            return Response(HttpStatusCode.OK, new
            {
                result = ids.Where(Points.ContainsKey).Select(id => new { id, payload = Points[id].GetProperty("payload") }).ToArray()
            });
        }

        private static HttpResponseMessage Response(HttpStatusCode status, object body) =>
            new(status)
            {
                Content = new StringContent(JsonSerializer.Serialize(body))
            };
    }
}
