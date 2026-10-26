using System.Net;
using System.Text.Json;
using AlfaHackaton.Result;
using ContextService.Common;
using ContextService.Contracts.Companies;
using ContextService.Contracts.Knowledge;
using ContextService.Contracts.Materials;
using ContextService.Contracts.Search;
using ContextService.Repositories.Interfaces;
using ContextService.Services;
using ContextService.Settings;
using Microsoft.Extensions.Configuration;
using Xunit;

public sealed class BankProfileSearchTests
{
    [Fact]
    public async Task RegionalMatchRanksHigherButMismatchRemainsARecipientWithoutDocumentaryCompanyProfile()
    {
        var matching = CompanyBankProfileTests.Profile();
        var mismatch = CompanyBankProfileTests.Profile(Guid.Parse("22222222-2222-4222-8222-222222222222"), "FixtureSouth", "OtherCity");
        var query = "FixtureNorth FixtureCity 10.11";
        var profiles = new[] { matching, mismatch };
        using var company = new CompanyKnowledgeFixture(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/company-service/internal/knowledge/companies")
            {
                return new { companies = Array.Empty<CompanyProfile>() };
            }
            var id = Guid.Parse(path.Split('/')[^1]);
            return profiles.Single(p => p.CompanyId == id);
        }));
        using var qdrant = new HttpClient(new AsyncHandler(async request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return new { status = "ok" };
            }
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var filter = body.RootElement.GetProperty("filter").GetProperty("must");
            Assert.Equal(2, filter.GetArrayLength());
            var kinds = filter[1].GetProperty("should").EnumerateArray().Select(x => x.GetProperty("match").GetProperty("value").GetString());
            Assert.Equal(new[] { "profile", "bank_profile" }, kinds);
            Assert.DoesNotContain("REGION", filter.GetRawText());
            return new
            {
                result = new
                {
                    points = profiles.Select(p => new
                    {
                        score = LexicalOverlap(query, CompanyBankProfileSearchText.Build(p)),
                        payload = new
                        {
                            kind = "bank_profile",
                            companyIds = new[] { p.CompanyId },
                            versionId = p.RevisionId,
                            hash = RetrievalEncoding.Hash(JsonSerializer.Serialize(p, ContextJson.Options)),
                            sourceRefs = p.SourceRefs,
                            text = CompanyBankProfileSearchText.Build(p)
                        }
                    })
                }
            };
        }));
        var search = CreateSearch(qdrant, company.Client);
        var before = JsonSerializer.Serialize(profiles, ContextJson.Options);

        var result = await search.Search(new(query, matching.KnownAt, Limit: 6), CancellationToken.None).RequireContentAsync();

        Assert.Equal(new[] { matching.CompanyId, mismatch.CompanyId }, result.Hits.Select(h => h.CompanyId));
        Assert.All(result.Hits, hit => Assert.Equal("bank_profile", hit.Kind));
        Assert.Equal(Assert.Single(matching.SourceRefs), Assert.Single(result.Hits[0].SourceRefs));
        Assert.Equal(before, JsonSerializer.Serialize(profiles, ContextJson.Options));
    }

    private static SearchService CreateSearch(HttpClient http, CompanyKnowledgeClient company)
    {
        var config = new ConfigurationManager { ["Qdrant:Collection"] = "fixture" };
        var qdrant = new QdrantHttpSettings { BaseAddress = "http://qdrant.fixture/" };
        var inference = new InferenceHttpSettings();
        http.BaseAddress = new(qdrant.BaseAddress);
        var client = new RetrievalClient(
            new RetrievalTransport<QdrantHttpSettings>(new AlfaHackaton.HttpClient.Http.HttpClient<QdrantHttpSettings>(http, qdrant), qdrant),
            new RetrievalTransport<InferenceHttpSettings>(new AlfaHackaton.HttpClient.Http.HttpClient<InferenceHttpSettings>(http, inference), inference), config);
        return new(client, null!, config, new EmptyMaterials(), company);
    }

    private static double LexicalOverlap(string query, string text)
    {
        var q = JsonSerializer.SerializeToElement(RetrievalEncoding.Sparse(query));
        var t = JsonSerializer.SerializeToElement(RetrievalEncoding.Sparse(text));
        var indices = q.GetProperty("indices").EnumerateArray().Select(x => x.GetUInt32()).ToHashSet();
        return t.GetProperty("indices").EnumerateArray().Select((x, i) => (x, i))
            .Where(x => indices.Contains(x.x.GetUInt32())).Sum(x => t.GetProperty("values")[x.i].GetDouble());
    }

    private sealed class Handler(Func<HttpRequestMessage, object> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response(request), ContextJson.Options))
            });
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<object>> response) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(await response(request), ContextJson.Options))
            };
    }

    private sealed class EmptyMaterials : IMaterialRepository
    {
        public Task<Result<MaterialVersion>> Save(MaterialVersion material) => throw new NotSupportedException();
        public Task<MaterialVersion?> GetAsync(string materialId, string versionId) => Task.FromResult<MaterialVersion?>(null);
        public Task<Result<CompanyEvidenceInventory>> CompanyEvidence(Guid companyId, DateTimeOffset asOf) => throw new NotSupportedException();
        public Task<IReadOnlyList<MaterialVersion>> GetContextMaterials(Guid companyId, string newsMaterialId, DateTimeOffset asOf) => throw new NotSupportedException();
        public Task<IReadOnlyList<MaterialVersion>> ListKnown(DateTimeOffset asOf) => Task.FromResult<IReadOnlyList<MaterialVersion>>([]);
    }
}
