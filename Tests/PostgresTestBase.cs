using AlfaHackaton.DapperContext;
using AlfaHackaton.DapperContext.Common.Interfaces.Dapper;
using ContextService.Migrations;
using ContextService.Repositories;
using ContextService.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

public abstract class PostgresTestBase : IAsyncLifetime
{
    protected string ConnectionString { get; private set; } = "";
    protected IDapperContext Database { get; private set; } = null!;
    private string schema = "";
    private string administrativeConnection = "";
    public virtual async Task InitializeAsync()
    {
        administrativeConnection = Environment.GetEnvironmentVariable("CONTEXT_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Set CONTEXT_TEST_POSTGRES to an isolated synthetic PostgreSQL instance before running integration tests.");
        schema = "context_test_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(administrativeConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE {schema}", connection);
            await command.ExecuteNonQueryAsync();
        }
        ConnectionString = new NpgsqlConnectionStringBuilder(administrativeConnection) { Database = schema, Pooling = false }.ToString();
        var settings = new DapperSettings { ConnectionString = ConnectionString };
        Database = new AlfaHackaton.DapperContext.Dapper.DapperContext(settings);
        var services = new ServiceCollection();
        services.AddMigrations(settings, typeof(InitialContext).Assembly);
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        using var provider = services.BuildServiceProvider();
        provider.UseMigrations();
    }
    protected PostgresMaterialRepository Repository() => new(Database, new CompanyEvidenceRepository(Database));
    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(schema))
        {
            return;
        }

        await using var connection = new NpgsqlConnection(administrativeConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE {schema} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
