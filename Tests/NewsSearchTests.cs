using System.Text.Json;
using ContextService.Common;
using ContextService.Contracts.Materials;
using ContextService.Contracts.News;
using ContextService.Repositories;
using ContextService.Services;
using Xunit;

public sealed class NewsSearchTests : PostgresTestBase
{
    private readonly DateTimeOffset at = DateTimeOffset.Parse("2026-10-25T10:00:00.0000001Z");
    private MaterialService Materials() => new(Repository(), 120000);
    private NewsSearchService Search() => new(new NewsSearchRepository(Database));
    private MaterialVersionRequest News(string material, string version = "v1") => new(
        material, version, "synthetic", "news", "Ставка", "Stored synthetic lead", at, at, null, [], [],
        new("uploaded", null, at, "lead"));

    [Fact]
    public async Task LatestPermittedVersionIsChosenBeforeMatchingAndPreciseAsOfIsPreserved()
    {
        var materials = Materials();
        var original = await materials.Save(News("correction")).RequireContentAsync();
        await materials.Save(News("correction", "v2") with
        {
            KnownAt = at.AddTicks(1), CorrectsVersionId = "v1", Title = "Unrelated", Text = "Changed"
        }).RequireContentAsync();
        var historical = await Search().Search(new("ставка", at.ToString("O")), CancellationToken.None).RequireContentAsync();
        Assert.Equal(at.UtcTicks, historical.AsOf.UtcTicks);
        Assert.Equal(original.ContentHash, Assert.Single(historical.Hits).ContentHash);
        var current = await Search().Search(new("ставка", at.AddTicks(1).ToString("O")), CancellationToken.None).RequireContentAsync();
        Assert.Empty(current.Hits);
        Assert.False(current.Exhaustive);
    }

    [Fact]
    public async Task PublicCorrectionHeadWinsSameTickRegardlessOfVersionLexicalOrder()
    {
        await Materials().Save(News("same-tick", "z-old")).RequireContentAsync();
        await Materials().Save(News("same-tick", "a-new") with
        {
            CorrectsVersionId = "z-old"
        }).RequireContentAsync();
        var response = await Search().Search(new("ставка", at.ToString("O")), CancellationToken.None).RequireContentAsync();
        Assert.Equal("a-new", Assert.Single(response.Hits).VersionId);
    }

    [Fact]
    public async Task PublicDescendantSupersedesItsPublicAncestorAcrossAPrivateBridge()
    {
        await Materials().Save(News("bridge", "z-old")).RequireContentAsync();
        await Materials().Save(News("bridge", "private") with
        {
            CorrectsVersionId = "z-old", CompanyIds = [Guid.NewGuid()]
        }).RequireContentAsync();
        await Materials().Save(News("bridge", "a-current") with
        {
            CorrectsVersionId = "private"
        }).RequireContentAsync();
        var response = await Search().Search(new("ставка", at.ToString("O")), CancellationToken.None).RequireContentAsync();
        var hit = Assert.Single(response.Hits);
        Assert.Equal("a-current", hit.VersionId);
        Assert.Empty(hit.CompanyIds);
    }

    [Fact]
    public async Task PrivateBankScopedFutureAndNonNewsMaterialsNeverBecomeCandidates()
    {
        var publicNews = await Materials().Save(News("public")).RequireContentAsync();
        await Materials().Save(News("foreign") with { CompanyIds = [Guid.NewGuid()] }).RequireContentAsync();
        await Materials().Save(News("bank") with { BankProfileIds = ["U_97"] }).RequireContentAsync();
        await Materials().Save(News("future") with { KnownAt = at.AddTicks(1) }).RequireContentAsync();
        await Materials().Save(News("market") with { Kind = "market" }).RequireContentAsync();
        await Materials().Save(News("public", "private-correction") with
        {
            CompanyIds = [Guid.NewGuid()], CorrectsVersionId = "v1"
        }).RequireContentAsync();
        var response = await Search().Search(new("ставка", at.ToString("O")), CancellationToken.None).RequireContentAsync();
        var hit = Assert.Single(response.Hits);
        Assert.Equal(publicNews.ContentHash, hit.ContentHash);
        Assert.Empty(hit.CompanyIds);
        Assert.Empty(hit.BankProfileIds);
        Assert.Equal(publicNews.Provenance, hit.Provenance);
        Assert.Equal("lead", hit.Provenance!.TextCompleteness);
        Assert.DoesNotContain("\"text\":", JsonSerializer.Serialize(hit, ContextJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public async Task LimitsStableOrderLiteralQueriesAndLegacyProvenanceArePreserved()
    {
        foreach (var name in new[] { "c", "b", "a" })
        {
            await Materials().Save(News(name) with { Provenance = null }).RequireContentAsync();
        }
        var response = await Search().Search(new("СТАВКА", at.ToString("O"), 2), CancellationToken.None).RequireContentAsync();
        Assert.Equal(["a", "b"], response.Hits.Select(h => h.MaterialId));
        Assert.All(response.Hits, h => Assert.Null(h.Provenance));
        foreach (var query in new[] { "%", "_", "' OR true --", "no-match" })
        {
            Assert.Empty((await Search().Search(new(query, at.ToString("O")), CancellationToken.None).RequireContentAsync()).Hits);
        }
    }

    [Fact]
    public async Task CancelledSearchCannotReturnSuccessfulEmptyResults()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(() => Search().Search(new("ставка", at.ToString("O")), new CancellationToken(true)));
    }
}
