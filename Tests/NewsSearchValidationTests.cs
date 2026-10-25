using System.Net;
using System.Text.Json;
using ContextService.Common;
using ContextService.Contracts.News;
using ContextService.Repositories.Interfaces;
using ContextService.Services;
using Xunit;

public sealed class NewsSearchValidationTests
{
    private sealed class Catalog : INewsSearchRepository
    {
        public int Calls { get; private set; }
        public string? Query { get; private set; }
        public DateTimeOffset AsOf { get; private set; }
        public Task<NewsSearchHit[]> Search(string query, DateTimeOffset asOf, int limit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            Query = query;
            AsOf = asOf;
            return Task.FromResult(Array.Empty<NewsSearchHit>());
        }
    }

    [Fact]
    public async Task InvalidRequestsAreRejectedBeforeRepositoryWork()
    {
        var catalog = new Catalog();
        var service = new NewsSearchService(catalog);
        var valid = new NewsSearchRequest("news", "2026-10-25T10:00:00Z");
        var invalid = new[]
        {
            valid with { Query = null }, valid with { Query = " " }, valid with { Query = new string('x', 2001) },
            valid with { Limit = 0 }, valid with { Limit = 7 }, valid with { AsOf = null },
            valid with { AsOf = "2026-10-25T10:00:00" }, valid with { AsOf = "2026-02-30T10:00:00Z" },
            valid with { AsOf = "2026-10-25T10:00:00.12345678Z" }
        };
        foreach (var request in invalid)
        {
            var error = await ResultAssert.ErrorAsync(() => service.Search(request, CancellationToken.None));
            Assert.Equal(HttpStatusCode.BadRequest, error.HttpStatusCode);
        }
        Assert.Equal(0, catalog.Calls);
    }

    [Fact]
    public async Task OffsetAndTicksSurviveAndEmptyCorpusIsExplicitlyNonExhaustive()
    {
        var catalog = new Catalog();
        var result = await new NewsSearchService(catalog).Search(new("  news  ", "2026-10-25T13:00:00.0000001+03:00"), CancellationToken.None).RequireContentAsync();
        Assert.Equal("news", catalog.Query);
        Assert.Equal(DateTimeOffset.Parse("2026-10-25T10:00:00.0000001Z").UtcTicks, catalog.AsOf.UtcTicks);
        Assert.Equal(catalog.AsOf, result.AsOf);
        Assert.False(result.Exhaustive);
        Assert.Empty(result.Hits);
    }

    [Fact]
    public void CallerCannotSupplyCompanyScopeOrAcquisitionParameters()
    {
        foreach (var property in new[] { "companyId", "publicOnly", "url", "userId" })
        {
            var body = "{\"query\":\"news\",\"asOf\":\"2026-10-25T10:00:00Z\",\"" + property + "\":\"untrusted\"}";
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NewsSearchRequest>(body, ContextJson.Options));
        }
    }
}
