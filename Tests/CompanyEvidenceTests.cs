using ContextService.Contracts.Materials;
using ContextService.Services;
using Xunit;

public sealed class CompanyEvidenceTests : PostgresTestBase
{
    [Fact]
    public async Task InventoryPreservesExactLatestForksAndExcludesFutureForeignAndPublicSources()
    {
        var repo = Repository();
        var service = new MaterialService(repo, 120000);
        var at = DateTimeOffset.Parse("2026-10-23T08:00:00Z");
        var company = Guid.NewGuid();
        async Task<MaterialVersion> Add(string id, string version, string kind, Guid[] scope, DateTimeOffset time, string? prior = null) =>
            await service.Save(new(id, version, id == "profile" ? "provided-bank-csv" : "fixture", kind, id,
                "Synthetic fixture original", time, time, prior, scope, []));
        var profile = await Add("profile", "1", "company", [company], at);
        await Add("own", "old", "company", [company], at);
        var latest = await Add("own", "new", "company", [company], at.AddSeconds(1), "old");
        await Add("own", "fork", "company", [company], at.AddSeconds(1), "old");
        await Add("own", "future", "company", [company], at.AddSeconds(3), "new");
        var retained = await Add("scope-changed", "own", "company", [company], at);
        await Add("scope-changed", "foreign-new", "company", [Guid.NewGuid()], at.AddSeconds(1), "own");
        await Add("foreign", "1", "company", [Guid.NewGuid()], at);
        await Add("public", "1", "market", [], at);
        var found = (await repo.CompanyEvidence(company, at.AddSeconds(2)));
        Assert.True(found.WatchAfterSequence < found.ChangeSequence);
        Assert.True(found.CompleteForLoadedScope);
        Assert.Equal(4, found.Sources.Length);
        Assert.Contains(found.Sources, s => s.MaterialId == "scope-changed" && s.ContentHash == retained.ContentHash);
        Assert.Contains(found.Sources, s => s.MaterialId == "profile" && s.SourceId == "provided-bank-csv" && s.ContentHash == profile.ContentHash);
        Assert.Contains(found.Sources, s => s.VersionId == "new" && s.ContentHash == latest.ContentHash);
        Assert.Contains(found.Sources, s => s.VersionId == "fork");
    }
}
