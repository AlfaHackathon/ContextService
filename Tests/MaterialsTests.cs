using ContextService.Contracts.Materials;
using ContextService.Models;
using ContextService.Services;
using Xunit;

public sealed class MaterialsTests : PostgresTestBase
{
    private readonly Guid company = Guid.NewGuid();
    private readonly DateTimeOffset now = DateTimeOffset.Parse("2026-10-21T08:00:00Z");
    private MaterialService Service(int budget = 120000) => new(Repository(), budget);
    private MaterialVersionRequest Material(string material = "news", string version = "v1", string kind = "news", Guid[]? scope = null)
        => new(material, version, "source-1", kind, "Synthetic test", "Synthetic material text", now, now,
            null, scope ?? [], []);

    [Fact]
    public async Task VersionIsImmutableAndSurvivesRepositoryRestart()
    {
        var request = Material();
        var first = (await Service().Save(request));
        Assert.Equal(first.ContentHash, (await Service().Save(request)).ContentHash);
        Assert.Empty((await Service().GetContext(new(company, "news", "v1", now))).Materials);
        Assert.Equal(first.ContentHash, (await Service().GetAsync("news", "v1")).ContentHash);
        var conflict = (await Assert.ThrowsAsync<ApiException>(async () => await Service().Save(request with { Text = "Changed text" })));
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task CorrectionRequiresActualPriorAndPreservesHistory()
    {
        var service = Service();
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(async () => await service.Save(Material(version: "v2") with { CorrectsVersionId = "v1" }))).StatusCode);
        await service.Save(Material());
        await service.Save(Material(version: "v2") with
        {
            CorrectsVersionId = "v1",
            Text = "Corrected synthetic news"
        });
        var bundle = (await Service().GetContext(new(company, "news", "v2", now)));
        Assert.Equal("v2", bundle.News.VersionId);
        Assert.Single(bundle.Materials, m => m.MaterialId == "news" && m.VersionId == "v1");
        Assert.True(bundle.Coverage.Complete);
    }

    [Fact]
    public async Task ContextUsesCompanyGuidAndKnowledgeTimeNotBankUid()
    {
        var service = Service();
        await service.Save(Material());
        await service.Save(Material("allowed", kind: "company", scope: [company]) with
        {
            BankProfileIds = ["U_97"]
        });
        await service.Save(Material("foreign", kind: "company", scope: [Guid.NewGuid()]) with
        {
            BankProfileIds = ["U_97"]
        });
        await service.Save(Material("future", kind: "company", scope: [company]) with
        {
            KnownAt = now.AddDays(1)
        });
        await service.Save(Material("market", kind: "market"));
        var context = (await service.GetContext(new(company, "news", "v1", now)));
        Assert.Equal(["allowed", "market"], context.Materials.Select(m => m.MaterialId).Order().ToArray());
        Assert.Equal(3, context.Coverage.MaterialCount);
    }

    [Fact]
    public async Task FutureNewsAndForeignScopedNewsAreRejected()
    {
        var service = Service();
        await service.Save(Material());
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(async () => await service.GetContext(new(company, "news", "v1", now.AddMinutes(-1))))).StatusCode);
        await service.Save(Material("foreign-news", scope: [Guid.NewGuid()]));
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(async () => await service.GetContext(new(company, "foreign-news", "v1", now)))).StatusCode);
    }

    [Fact]
    public async Task BudgetFailureNeverReturnsPartialContext()
    {
        await Service().Save(Material());
        var failure = (await Assert.ThrowsAsync<ApiException>(async () => await Service(5).GetContext(new(company, "news", "v1", now))));
        Assert.Equal(422, failure.StatusCode);
        Assert.Equal("context_budget_exceeded", failure.Code);
    }

    [Fact]
    public async Task ScopeOrderingAndTimezoneDoNotChangeCanonicalHash()
    {
        var other = Guid.NewGuid();
        var r = Material(scope: [company, other]);
        var first = (await Service().Save(r));
        var same = (await Service().Save(r with
        {
            CompanyIds = [other, company, company],
            KnownAt = now.ToOffset(TimeSpan.FromHours(3)),
            BankProfileIds = []
        }));
        Assert.Equal(first.ContentHash, same.ContentHash);
    }

    [Fact]
    public async Task UploadProvenanceSurvivesRestartAndCannotBecomeWebAcquisitionOrSilentMetadataChange()
    {
        var request = Material() with
        {
            Provenance = new("uploaded", null, now, "excerpt", null, "bank-report.pdf page 1 OCR")
        };
        var first = (await Service().Save(request));
        Assert.Equal(request.Provenance, (await Service().GetAsync("news", "v1")).Provenance);
        Assert.Equal(first.ContentHash, (await Service().Save(request with
        {
            Provenance = request.Provenance! with
            {
                RetrievedAt = now.ToOffset(TimeSpan.FromHours(3))
            }
        })).ContentHash);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(async () => await Service().Save(request with { Provenance = request.Provenance! with { TextCompleteness = "full" } }))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(async () => await Service().Save(Material("bad-origin") with { Provenance = new("news_service", "https://news.example/source", now, "full", "other-version") }))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(async () => await Service().Save(Material("future-retrieval") with { Provenance = new("uploaded", null, now.AddTicks(1), "full") }))).StatusCode);
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
        Assert.Equal(legacyHash, (await Service().Save(request)).ContentHash);
    }

    [Fact]
    public async Task CurrentNewsEventMayContainOnlyTitleButCannotClaimFullOriginal()
    {
        var request = Material() with
        {
            Text = "",
            Provenance = new("event_payload", null, now, "lead", null, "tass-external-id")
        };
        var saved = (await Service().Save(request));
        Assert.Equal("", saved.Text);
        Assert.Equal("lead", saved.Provenance!.TextCompleteness);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(async () => await Service().Save(Material("not-full") with { Text = "", Provenance = request.Provenance with { TextCompleteness = "full" } }))).StatusCode);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(async () => await Service().Save(Material("not-company", kind: "company", scope: [company]) with { Text = "", Provenance = request.Provenance }))).StatusCode);
    }

}
