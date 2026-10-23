using ContextService.Contracts.Materials;
using ContextService.Services;
using Xunit;

public sealed class MaterialsTests : PostgresTestBase
{
    private readonly Guid company = Guid.NewGuid();
    private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-10-21T08:00:00Z");
    private MaterialService Service(int budget = 120000) => new(Repository(), budget);
    private MaterialVersionRequest Material(string material = "news", string version = "v1", string kind = "news", Guid[]? scope = null) => new(material, version, "source-1", kind, "Synthetic test", "Synthetic material text", now, now, null, scope ?? [], []);
    [Fact]
    public async Task VersionIsImmutableAndSurvivesRepositoryRestart()
    {
        var request = Material();
        var first = (await (Service().Save(request)).RequireContentAsync());
        Assert.Equal(first.ContentHash, (await (Service().Save(request)).RequireContentAsync()).ContentHash);
        Assert.Empty((await (Service().GetContext(new(company, "news", "v1", now))).RequireContentAsync()).Materials);
        Assert.Equal(first.ContentHash, (await (Service().GetAsync("news", "v1")).RequireContentAsync()).ContentHash);
        var conflict = (await ResultAssert.ErrorAsync(async () => await Service().Save(request with { Text = "Changed text" })));
        Assert.Equal(409, (int)conflict.HttpStatusCode!);
    }

    [Fact]
    public async Task CorrectionRequiresActualPriorAndPreservesHistory()
    {
        var service = Service();
        Assert.Equal(409, (int)(await ResultAssert.ErrorAsync(async () => await service.Save(Material(version: "v2") with { CorrectsVersionId = "v1" }))).HttpStatusCode!);
        await (service.Save(Material())).RequireContentAsync();
        await (service.Save(Material(version: "v2") with
        {
            CorrectsVersionId = "v1",
            Text = "Corrected synthetic news"
        })).RequireContentAsync();
        var bundle = (await (Service().GetContext(new(company, "news", "v2", now))).RequireContentAsync());
        Assert.Equal("v2", bundle.News.VersionId);
        Assert.Single(bundle.Materials, m => m.MaterialId == "news" && m.VersionId == "v1");
        Assert.True(bundle.Coverage.Complete);
    }

    [Fact]
    public async Task ContextUsesCompanyGuidAndKnowledgeTimeNotBankUid()
    {
        var service = Service();
        await (service.Save(Material())).RequireContentAsync();
        await (service.Save(Material("allowed", kind: "company", scope: [company]) with
        {
            BankProfileIds = ["U_97"]
        })).RequireContentAsync();
        await (service.Save(Material("foreign", kind: "company", scope: [Guid.NewGuid()]) with
        {
            BankProfileIds = ["U_97"]
        })).RequireContentAsync();
        await (service.Save(Material("future", kind: "company", scope: [company]) with
        {
            KnownAt = now.AddDays(1)
        })).RequireContentAsync();
        await (service.Save(Material("market", kind: "market"))).RequireContentAsync();
        var context = (await (service.GetContext(new(company, "news", "v1", now))).RequireContentAsync());
        Assert.Equal(["allowed", "market"], context.Materials.Select(m => m.MaterialId).Order().ToArray());
        Assert.Equal(3, context.Coverage.MaterialCount);
    }

    [Fact]
    public async Task FutureNewsAndForeignScopedNewsAreRejected()
    {
        var service = Service();
        await (service.Save(Material())).RequireContentAsync();
        Assert.Equal(403, (int)(await ResultAssert.ErrorAsync(async () => await service.GetContext(new(company, "news", "v1", now.AddMinutes(-1))))).HttpStatusCode!);
        await (service.Save(Material("foreign-news", scope: [Guid.NewGuid()]))).RequireContentAsync();
        Assert.Equal(403, (int)(await ResultAssert.ErrorAsync(async () => await service.GetContext(new(company, "foreign-news", "v1", now)))).HttpStatusCode!);
    }

    [Fact]
    public async Task BudgetFailureNeverReturnsPartialContext()
    {
        await (Service().Save(Material())).RequireContentAsync();
        var failure = (await ResultAssert.ErrorAsync(async () => await Service(5).GetContext(new(company, "news", "v1", now))));
        Assert.Equal(422, (int)failure.HttpStatusCode!);
        Assert.Contains("configured limit is 5. No partial context was returned.", failure.Title);
    }

    [Fact]
    public async Task ScopeOrderingAndTimezoneDoNotChangeCanonicalHash()
    {
        var other = Guid.NewGuid();
        var r = Material(scope: [company, other]);
        var first = (await (Service().Save(r)).RequireContentAsync());
        var same = (await (Service().Save(r with
        {
            CompanyIds = [other, company, company],
            KnownAt = now.ToOffset(TimeSpan.FromHours(3)),
            BankProfileIds = []
        })).RequireContentAsync());
        Assert.Equal(first.ContentHash, same.ContentHash);
    }

    [Fact]
    public async Task UploadProvenanceSurvivesRestartAndCannotBecomeWebAcquisitionOrSilentMetadataChange()
    {
        var request = Material() with
        {
            Provenance = new("uploaded", null, now, "excerpt", null, "bank-report.pdf page 1 OCR")
        };
        var first = (await (Service().Save(request)).RequireContentAsync());
        Assert.Equal(request.Provenance, (await (Service().GetAsync("news", "v1")).RequireContentAsync()).Provenance);
        Assert.Equal(first.ContentHash, (await (Service().Save(request with
        {
            Provenance = request.Provenance! with
            {
                RetrievedAt = now.ToOffset(TimeSpan.FromHours(3))
            }
        })).RequireContentAsync()).ContentHash);
        Assert.Equal(409, (int)(await ResultAssert.ErrorAsync(async () => await Service().Save(request with { Provenance = request.Provenance! with { TextCompleteness = "full" } }))).HttpStatusCode!);
        Assert.Equal(400, (int)(await ResultAssert.ErrorAsync(async () => await Service().Save(Material("bad-origin") with { Provenance = new("news_service", "https://news.example/source", now, "full", "other-version") }))).HttpStatusCode!);
        Assert.Equal(400, (int)(await ResultAssert.ErrorAsync(async () => await Service().Save(Material("future-retrieval") with { Provenance = new("uploaded", null, now.AddTicks(1), "full") }))).HttpStatusCode!);
    }

    [Fact]
    public async Task AbsentProvenanceKeepsLegacyCanonicalHash()
    {
        var request = Material();
        var legacyJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            request.MaterialId,
            request.VersionId,
            request.SourceId,
            request.Kind,
            request.Title,
            request.Text,
            request.KnownAt,
            request.PublishedAt,
            request.CorrectsVersionId,
            request.CompanyIds,
            request.BankProfileIds
        }, MaterialService.Json);
        var legacyHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(legacyJson)));
        Assert.Equal(legacyHash, (await (Service().Save(request)).RequireContentAsync()).ContentHash);
    }

    [Fact]
    public async Task CurrentNewsEventMayContainOnlyTitleButCannotClaimFullOriginal()
    {
        var request = Material() with
        {
            Text = "",
            Provenance = new("event_payload", null, now, "lead", null, "tass-external-id")
        };
        var saved = (await (Service().Save(request)).RequireContentAsync());
        Assert.Equal("", saved.Text);
        Assert.Equal("lead", saved.Provenance!.TextCompleteness);
        Assert.Equal(400, (int)(await ResultAssert.ErrorAsync(async () => await Service().Save(Material("not-full") with { Text = "", Provenance = request.Provenance with { TextCompleteness = "full" } }))).HttpStatusCode!);
        Assert.Equal(400, (int)(await ResultAssert.ErrorAsync(async () => await Service().Save(Material("not-company", kind: "company", scope: [company]) with { Text = "", Provenance = request.Provenance }))).HttpStatusCode!);
    }
}
