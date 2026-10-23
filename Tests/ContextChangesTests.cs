using ContextService.Contracts.Knowledge;
using ContextService.Contracts.Materials;
using ContextService.Models;
using ContextService.Repositories;
using ContextService.Services;
using Xunit;

public sealed class ContextChangesTests : PostgresTestBase
{
    [Fact]
    public async Task ImmutableSourceAndKnowledgeWritesProduceDurableOrderedEventsOnce()
    {
        var repository = Repository();
        var materials = new MaterialService(repository, 120000);
        var at = DateTimeOffset.Parse("2026-10-22T10:00:00Z");
        var request = new MaterialVersionRequest("synthetic-terms", "1", "fixture", "market", "Fixture", "Synthetic terms", at, at, null, [], []);
        var source = (await materials.Save(request));
        await materials.Save(request);
        var knowledge = new KnowledgeService(new KnowledgeStore(Database), materials);
        var reference = new SourceReference(source.MaterialId, source.VersionId, source.ContentHash);
        var group = new GroupProfile("synthetic-group", "1", "Fixture", at, [], [reference]);
        await knowledge.SaveGroup(group);
        await knowledge.SaveGroup(group);
        await Assert.ThrowsAsync<ApiException>(async () => await materials.Save(request with { Text = "conflicting original" }));
        var events = new ContextChanges(Database);
        var first = Assert.Single((await events.Read(0)));
        Assert.Equal("material:market", first.Category);
        var second = Assert.Single((await new ContextChanges(Database).Read(first.Sequence)));
        Assert.Equal("group", second.Category);
        Assert.Empty((await events.Read(second.Sequence)));
    }
}
