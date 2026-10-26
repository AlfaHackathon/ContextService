using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContextService.Contracts.Materials;
using ContextService.Controllers;
using ContextService.Extensions;
using ContextService.Middleware;
using ContextService.Repositories.Interfaces;
using ContextService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

// HTTP contract only, with an explicit repository fake. No PostgreSQL/inference environment.
public sealed class CompanyMaterialsMvcTests
{
    private static async Task<WebApplication> Start(CompanyMaterialListTests.Catalog catalog)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Internal:Secret"] = "synthetic-material-list-only",
            ["Swagger:Enabled"] = "true"
        });
        builder.Services.AddSingleton<ICompanyMaterialListRepository>(catalog);
        builder.Services.AddSingleton<CompanyMaterialListService>();
        builder.Services.AddControllers(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .AddApplicationPart(typeof(CompanyMaterialsController).Assembly)
            .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(new
            {
                code = "invalid_request",
                message = "The request body or parameters are invalid."
            }));
        builder.Services.AddServiceSwagger();
        var app = builder.Build();
        app.UseContextRequestHandling();
        app.UseServiceSwagger();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task RouteRequiresInternalCredentialThenBindsExplicitScopeAndReturnsOnlyMetadata()
    {
        var company = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var at = DateTimeOffset.Parse("2026-10-25T10:00:00.0000001Z");
        var catalog = new CompanyMaterialListTests.Catalog
        {
            Rows = [new("own", "v1", "synthetic-upload", "company", "Own company contract", at, null, null,
                new string('a', 64), "full", 1, new("uploaded", null, at, "full"))]
        };
        await using var app = await Start(catalog);
        using var client = app.GetTestClient();
        var route = $"/context-service/internal/companies/{company:D}/materials?asOf=2026-10-25T10:00:00.0000001Z&bankProfileId=U_1&limit=1";
        client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-user-token");
        using var denied = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, catalog.Calls);
        client.DefaultRequestHeaders.Add("X-Internal-Secret", "synthetic-material-list-only");
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(company.ToString("D"), page.GetProperty("companyId").GetString());
        Assert.Equal("canonical_changes_only", page.GetProperty("coverage").GetString());
        Assert.Equal(at.UtcTicks, page.GetProperty("asOf").GetDateTimeOffset().UtcTicks);
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextCursor").ValueKind);
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("own", item.GetProperty("materialId").GetString());
        Assert.Equal("full", item.GetProperty("provenance").GetProperty("textCompleteness").GetString());
        Assert.False(item.TryGetProperty("text", out _));
        Assert.False(item.TryGetProperty("companyIds", out _));
        Assert.False(item.TryGetProperty("bankProfileIds", out _));
        Assert.Equal(company, catalog.CompanyId);
        Assert.Equal("U_1", catalog.Bank);
        Assert.Equal(1, catalog.Limit);
        Assert.Equal(at.UtcTicks, catalog.AsOf.UtcTicks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?asOf=garbage")]
    [InlineData("?asOf=2026-10-25T10:00:00")]
    [InlineData("?asOf=2026-10-25T10:00:00Z&limit=0")]
    [InlineData("?asOf=2026-10-25T10:00:00Z&limit=21")]
    [InlineData("?asOf=2026-10-25T10:00:00Z&limit=invalid")]
    [InlineData("?asOf=2026-10-25T10:00:00Z&cursor=invalid+cursor")]
    public async Task InvalidBindingAndCursorCannotBecomeSuccessfulEmptyPage(string query)
    {
        var catalog = new CompanyMaterialListTests.Catalog();
        await using var app = await Start(catalog);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Internal-Secret", "synthetic-material-list-only");
        using var response = await client.GetAsync("/context-service/internal/companies/11111111-1111-4111-8111-111111111111/materials" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, catalog.Calls);
    }

    [Fact]
    public async Task SwaggerDocumentsOnlyAnInternalScopedMetadataPage()
    {
        await using var app = await Start(new());
        using var client = app.GetTestClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/context-service/swagger/v1/swagger.json");
        var operation = document.GetProperty("paths").GetProperty("/context-service/internal/companies/{companyId}/materials").GetProperty("get");
        Assert.True(operation.GetProperty("security")[0].TryGetProperty("InternalSecret", out _));
        Assert.True(operation.GetProperty("responses").GetProperty("200").TryGetProperty("content", out _));
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var summary = schemas.GetProperty("CompanyMaterialSummary").GetProperty("properties");
        Assert.True(summary.TryGetProperty("contentHash", out _));
        Assert.True(summary.TryGetProperty("latestVersionCount", out _));
        Assert.False(summary.TryGetProperty("text", out _));
        Assert.False(summary.TryGetProperty("companyIds", out _));
        Assert.False(summary.TryGetProperty("bankProfileIds", out _));
    }
}
