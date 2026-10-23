using ContextService.Contracts.Knowledge;
using ContextService.Contracts.Materials;
using ContextService.Repositories;
using ContextService.Services;
using System.Net;
using System.Text.Json;
using ContextService;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class RetrievedContextTests : PostgresTestBase
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-10-21T08:00:00Z");

    [Fact]
    public async Task OldTopRankedChunkAlsoReturnsNewestKnownCorrectionButNeverFutureVersion()
    {
        var repository = Repository();
        var materials = new MaterialService(repository, 120000);
        var company = Guid.NewGuid();
        var news = (await materials.Save(new("news", "1", "fixture", "news", "Event", "Public event", Time, Time, null, [], [])));
        var old = (await materials.Save(new("terms", "1", "fixture", "company", "Terms", "Old margin 2", Time, Time, null, [company], [])));
        await materials.Save(new("terms", "2", "fixture", "company", "Terms", "Corrected margin 3", Time.AddSeconds(1), Time, "1", [company], []));
        await materials.Save(new("terms", "10", "fixture", "company", "Terms", "Equal-time correction margin 3.5", Time.AddSeconds(1), Time, "2", [company], []));
        await materials.Save(new("terms", "3", "fixture", "company", "Terms", "Future margin 4", Time.AddSeconds(3), Time, "2", [company], []));
        var knowledge = new KnowledgeService(new KnowledgeStore(Database), materials);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Qdrant:BaseUrl"] = "http://qdrant.fixture", ["Qdrant:Collection"] = "fixture" }).Build();
        using var client = new HttpClient(new QdrantHandler(old, company));
        var search = CreateSearch(client, config, repository, knowledge, Database);
        var service = new RetrievedContextService(materials, repository, knowledge, search, config);
        var ranked = await search.Search(new("terms", Time.AddSeconds(2), company, 1), CancellationToken.None);
        Assert.Equal(new[] { "10", "2" }, Assert.Single(ranked.Hits).RelatedVersions!.Select(v => v.VersionId).Order());
        var result = await service.Get(new(company, news.MaterialId, news.VersionId, Time.AddSeconds(2), ["terms"], 1), CancellationToken.None);
        Assert.Equal(new[] { "1", "10", "2" }, result.Materials.Select(m => m.VersionId).Order());
        Assert.Contains(result.Materials, m => m.Text == "Corrected margin 3");
        var historical = await service.Get(new(company, news.MaterialId, news.VersionId, Time, ["terms"], 1), CancellationToken.None);
        Assert.Equal("1", Assert.Single(historical.Materials).VersionId);
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => client; }

    private static SearchService CreateSearch(HttpClient http, IConfiguration config, PostgresMaterialRepository repository,
        KnowledgeService knowledge, AlfaHackaton.DapperContext.Common.Interfaces.Dapper.IDapperContext database)
    {
        var qdrantSettings = new ContextService.Settings.QdrantHttpSettings { BaseAddress = "http://qdrant.fixture/" };
        var inferenceSettings = new ContextService.Settings.InferenceHttpSettings();
        http.BaseAddress = new Uri(qdrantSettings.BaseAddress);
        var client = new RetrievalClient(
            new RetrievalTransport<ContextService.Settings.QdrantHttpSettings>(new AlfaHackaton.HttpClient.Http.HttpClient<ContextService.Settings.QdrantHttpSettings>(http, qdrantSettings), qdrantSettings),
            new RetrievalTransport<ContextService.Settings.InferenceHttpSettings>(new AlfaHackaton.HttpClient.Http.HttpClient<ContextService.Settings.InferenceHttpSettings>(http, inferenceSettings), inferenceSettings), config);
        var projection = new SearchProjectionService(client, new SearchProjectionRepository(database), config, repository, knowledge);
        return new SearchService(client, projection, config, repository, knowledge);
    }

    [Fact]
    public async Task CyrillicOriginalUsesDecodedContextBudgetWithoutChangingSourceHash()
    {
        var repository = Repository();
        var materials = new MaterialService(repository, 120000);
        var company = Guid.NewGuid();
        var news = (await materials.Save(new("news", "1", "fixture", "news", "Новость", "Событие", Time, Time, null, [], [])));
        var original = (await materials.Save(new("bank", "1", "fixture", "market", "Аналитика", new string('я', 25000), Time, Time, null, [], [])));
        var knowledge = new KnowledgeService(new KnowledgeStore(Database), materials);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Qdrant:BaseUrl"] = "http://qdrant.fixture", ["Qdrant:Collection"] = "fixture" }).Build();
        using var client = new HttpClient(new QdrantHandler(original, company));
        var service = new RetrievedContextService(materials, repository, knowledge,
            CreateSearch(client, config, repository, knowledge, Database), config);
        var result = await service.Get(new(company, news.MaterialId, news.VersionId, Time, ["bank"]), CancellationToken.None);
        var returned = Assert.Single(result.Materials);
        Assert.Equal(original.Text, returned.Text);
        Assert.Equal(original.ContentHash, returned.ContentHash);
        Assert.InRange(result.Coverage.Characters, 25000, 30000);
    }
    private sealed class QdrantHandler(MaterialVersion old, Guid company) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object result = request.Method == HttpMethod.Get ? new { status = "ok" } : new
            { result = new { points = new[] { new { score = 1.0, payload = new { kind = "company", companyIds = new[] { company }, materialId = old.MaterialId, versionId = old.VersionId, hash = old.ContentHash, text = old.Text, sourceRefs = new[] { new SourceReference(old.MaterialId, old.VersionId, old.ContentHash) } } } } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result, MaterialService.Json)) });
        }
    }
}
