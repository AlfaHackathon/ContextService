using System.Reflection;
using System.Text.Json;
using ContextService.Common;
using ContextService.Contracts.Knowledge;
using ContextService.Contracts.Materials;
using ContextService.Repositories;
using ContextService.Resources;
using ContextService.Services;
using Npgsql;
using Xunit;

public sealed class PostgresStorageTests : PostgresTestBase
{
    private readonly DateTimeOffset known = DateTimeOffset.Parse("2026-10-21T08:00:00.0000001Z");
    private MaterialVersionRequest Request => new("atomic", "v1", "source", "company", "Atomic", "Text", known, null, null, [Guid.NewGuid()], []);

    [Fact]
    public void EveryTypedSqlResourceIsEmbeddedAndFailsFastWhenMissing()
    {
        Assert.Contains("ContextService.Resources.ContextSql.resources", typeof(ContextSql).Assembly.GetManifestResourceNames());
        foreach (var property in typeof(ContextSql).GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(null)));
        }

        var get = typeof(ContextSql).GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.IsType<InvalidOperationException>(Assert.Throws<TargetInvocationException>(() => get.Invoke(null, ["missing_resource"])).InnerException);
    }

    [Fact]
    public async Task MigrationReopenPreservesCanonicalPayloadHashTicksAndIndexes()
    {
        var service = new MaterialService(Repository(), 120000);
        var saved = await (service.Save(Request)).RequireContentAsync();
        var reopened = await Repository().GetAsync(saved.MaterialId, saved.VersionId);
        Assert.Equal(saved.ContentHash, reopened!.ContentHash);
        Assert.Equal(known.UtcTicks, reopened.KnownAt.UtcTicks);
        Assert.Equal(JsonSerializer.Serialize(saved, ContextJson.Options), JsonSerializer.Serialize(reopened, ContextJson.Options));
        await using var connection = await Open();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_indexes WHERE schemaname=current_schema() AND indexname IN ('ix_materials_known','ix_material_companies','ix_knowledge_asof')", connection);
        Assert.Equal(3L, await command.ExecuteScalarAsync());
        var change = Assert.Single(await (new ContextChanges(Database).Read(0)).RequireContentAsync());
        Assert.Equal(known.UtcTicks, change.KnownAt.UtcTicks);
    }

    [Fact]
    public async Task ChangeAppendFailureRollsBackMaterialScopeAndKnowledgeWithIdempotentRetry()
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand("""
            CREATE FUNCTION fail_change() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected change failure'; END $$;
            CREATE TRIGGER fail_change BEFORE INSERT ON context_changes FOR EACH ROW EXECUTE FUNCTION fail_change();
            """, connection);
        await command.ExecuteNonQueryAsync();
        var request = Request;
        var service = new MaterialService(Repository(), 120000);
        await Assert.ThrowsAsync<PostgresException>(() => (service.Save(request)).RequireContentAsync());
        Assert.Null(await Repository().GetAsync("atomic", "v1"));
        command.CommandText = "SELECT count(*) FROM material_companies";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        var knowledge = new KnowledgeStore(Database);
        await Assert.ThrowsAsync<PostgresException>(() => (knowledge.Save("group", "g", "1", known, new GroupProfile("g", "1", "Group", known, [], []))).RequireContentAsync());
        Assert.Empty(await knowledge.List<GroupProfile>("group", known));
        command.CommandText = "DROP TRIGGER fail_change ON context_changes";
        await command.ExecuteNonQueryAsync();
        Assert.Equal((await (service.Save(request)).RequireContentAsync()).ContentHash, (await (service.Save(request)).RequireContentAsync()).ContentHash);
        Assert.Single(await (new ContextChanges(Database).Read(0)).RequireContentAsync());
    }

    [Fact]
    public async Task ConcurrentDuplicateWritesHaveOneCanonicalVersionAndChange()
    {
        var service = new MaterialService(Repository(), 120000);
        var request = Request;
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => (service.Save(request)).RequireContentAsync()));
        Assert.Single(results.Select(result => result.ContentHash).Distinct());
        Assert.Single(await (new ContextChanges(Database).Read(0)).RequireContentAsync());
        var failure = await ResultAssert.ErrorAsync(() => service.Save(request with { Text = "conflict" }));
        Assert.Equal("This material/version already exists with different content or metadata.", failure.Title);
    }

    [Fact]
    public async Task EqualKnowledgeTimeConflictLeavesOriginalAndChangeUnchanged()
    {
        var store = new KnowledgeStore(Database);
        var first = new GroupProfile("g", "1", "Group", known, [], []);
        await (store.Save("group", "g", "1", known, first)).RequireContentAsync();
        var failure = await ResultAssert.ErrorAsync(() => store.Save("group", "g", "2", known, first with { RevisionId = "2" }));
        Assert.Equal("Two different revisions cannot share one knowledge time.", failure.Title);
        Assert.Equal("1", Assert.Single(await store.List<GroupProfile>("group", known)).RevisionId);
        Assert.Single(await (new ContextChanges(Database).Read(0)).RequireContentAsync());
    }

    [Fact]
    public async Task ProjectionBatchFailureRollsBackAndReopenKeepsSuccessfulRetry()
    {
        var repository = new SearchProjectionRepository(Database);
        IEnumerable<KeyValuePair<string, string>> Interrupted()
        {
            yield return new("first", "h1");
            throw new InvalidOperationException("Injected interrupted batch");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.Mark("pipeline", Interrupted()));
        Assert.Empty(await new SearchProjectionRepository(Database).Read("pipeline"));
        await repository.Mark("pipeline", [new("first", "h1"), new("second", "h2")]);
        Assert.Equal(2, (await new SearchProjectionRepository(Database).Read("pipeline")).Count);
        Assert.Empty(await repository.Read("other-pipeline"));
    }

    private async Task<NpgsqlConnection> Open()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
