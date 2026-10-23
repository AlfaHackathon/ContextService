using ContextService.Contracts.Materials;
using ContextService.Repositories;
using ContextService.Services;
using Xunit;

public sealed class ContextChangesTests : PostgresTestBase
{
    [Fact]
    public async Task ImmutableMaterialWritesProduceDurableEventsOnce()
    {
        var repository = Repository();
        var materials = new MaterialService(repository, 120000);
        var at = DateTimeOffset.Parse("2026-10-22T10:00:00Z");
        var request = new MaterialVersionRequest("synthetic-terms", "1", "fixture", "market", "Fixture", "Synthetic terms", at, at, null, [], []);
        var source = (await (materials.Save(request)).RequireContentAsync());
        await (materials.Save(request)).RequireContentAsync();
        await ResultAssert.ErrorAsync(async () => await materials.Save(request with { Text = "conflicting original" }));
        var events = new ContextChanges(Database);
        var first = Assert.Single((await (events.Read(0)).RequireContentAsync()));
        Assert.Equal("material:market", first.Category);
        Assert.Empty((await (events.Read(first.Sequence)).RequireContentAsync()));
    }
}
