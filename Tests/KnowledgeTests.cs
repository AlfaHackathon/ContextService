using ContextService.Contracts.Knowledge;
using ContextService.Models;
using ContextService.Repositories;
using ContextService.Services;
using ContextService;
using Xunit;

public sealed class KnowledgeTests : PostgresTestBase
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
    private MaterialService materials = null!;
    private KnowledgeService service = null!;
    private SourceReference evidence = null!;
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        materials = new(Repository(), 120000);
        var source = (await materials.Save(new("group-terms", "1", "synthetic", "market", "Explicit reporting perimeter",
            "Root includes A and nested group. Nested group includes A and B.", Time, Time, null, [], [])));
        evidence = new(source.MaterialId, source.VersionId, source.ContentHash);
        service = new(new KnowledgeStore(Database), materials);
    }
    [Fact]
    public async Task Nested_union_keeps_root_own_companies_and_deduplicates_multiple_paths()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await service.SaveGroup(new("nested", "1", "Nested", Time, [new("company", a.ToString(), [evidence]), new("company", b.ToString(), [evidence])], [evidence]));
        await service.SaveGroup(new("root", "1", "Root", Time, [new("company", a.ToString(), [evidence]), new("group", "nested", [evidence])], [evidence]));
        var result = (await service.Expand("root", Time));
        Assert.True(result.Complete); Assert.Equal(2, result.CompanyIds.Length); Assert.Equal(3, result.Paths.Length);
        Assert.Equal(2, result.Paths.Count(x => x.CompanyId == a));
    }
    [Fact]
    public async Task Three_levels_preserve_root_and_intermediate_own_obligations()
    {
        var rootOwner = Guid.NewGuid(); var intermediateOwner = Guid.NewGuid(); var leafOwner = Guid.NewGuid();
        await service.SaveGroup(new("leaf", "1", "Leaf", Time, [new("company", leafOwner.ToString(), [evidence])], [evidence]));
        await service.SaveGroup(new("middle", "1", "Middle", Time,
            [new("company", intermediateOwner.ToString(), [evidence]), new("company", leafOwner.ToString(), [evidence]), new("group", "leaf", [evidence])], [evidence]));
        await service.SaveGroup(new("root", "1", "Root", Time,
            [new("company", rootOwner.ToString(), [evidence]), new("group", "middle", [evidence])], [evidence]));
        var result = (await service.Expand("root", Time));
        Assert.True(result.Complete);
        Assert.Equal(new[] { rootOwner, intermediateOwner, leafOwner }.Order(), result.CompanyIds);
        Assert.Equal(4, result.Paths.Length);
        Assert.Contains(result.Paths, p => p.CompanyId == rootOwner && p.GroupIds.SequenceEqual(["root"]));
        Assert.Contains(result.Paths, p => p.CompanyId == intermediateOwner && p.GroupIds.SequenceEqual(["root", "middle"]));
        Assert.Equal(2, result.Paths.Count(p => p.CompanyId == leafOwner));
    }
    [Fact]
    public async Task Missing_members_and_cycles_are_coverage_gaps_and_asof_never_sees_future_membership()
    {
        await service.SaveGroup(new("root", "1", "Root", Time, [new("group", "missing", [evidence])], [evidence]));
        var before = (await service.Expand("root", Time));
        Assert.False(before.Complete); Assert.Equal(["missing"], before.MissingGroups);
        await service.SaveGroup(new("missing", "1", "Later", Time.AddDays(1), [new("group", "root", [evidence])], [evidence]));
        Assert.Equal(["missing"], (await service.Expand("root", Time)).MissingGroups);
        Assert.Single((await service.Expand("root", Time.AddDays(1))).Cycles);
        Assert.False((await service.Expand("root", Time.AddDays(1))).Complete);
    }
    [Fact]
    public async Task Profiles_need_own_originals_and_known_hashes_and_survive_reopen()
    {
        var company = Guid.NewGuid();
        var doc = (await materials.Save(new("profile", "1", "synthetic", "company", "Company", "Manufactures paper. Uses gas.", Time, Time, null, [company], [])));
        var own = new SourceReference(doc.MaterialId, doc.VersionId, doc.ContentHash);
        var profile = new CompanyProfile(company, "1", "U_1", "Paper", "Paper mill", ["17"], ["gas"], Time, [own]);
        await service.SaveCompany(profile);
        await Assert.ThrowsAsync<ApiException>(async () => await service.SaveCompany(profile with { RevisionId = "2", SourceRefs = [evidence] }));
        await Assert.ThrowsAsync<ApiException>(async () => await service.SaveCompany(profile with { RevisionId = "2", KnownAt = Time.AddDays(1), SourceRefs = [own with { ContentHash = new string('0', 64) }] }));
        var reopened = new KnowledgeService(new KnowledgeStore(Database), materials);
        Assert.Empty((await reopened.Companies(Time.AddTicks(-1))));
        Assert.Equal("Paper mill", Assert.Single((await reopened.Companies(Time))).Description);
        await Assert.ThrowsAsync<ApiException>(async () => await service.SaveCompany(profile with { Description = "Changed" }));
    }
}
