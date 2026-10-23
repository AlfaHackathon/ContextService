using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AlfaHackaton.Controllers.Controllers;
using ContextService.Common;
using ContextService.Contracts.Materials;
using ContextService.Controllers;
using ContextService.Extensions;
using ContextService.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

public sealed class MvcContractTests : PostgresTestBase
{
    private async Task<WebApplication> Start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            ["Internal:Secret"] = "synthetic-mvc-secret"
        });
        builder.Services.AddContextServices(builder.Configuration, builder.Environment);
        builder.Services.AddControllers().AddApplicationPart(typeof(MaterialsController).Assembly);
        var app = builder.Build();
        app.UseContextRequestHandling();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task ActualMvcPreservesAdmissionBindingErrorsAndMaterialResponse()
    {
        Assert.True(typeof(BaseController).IsAssignableFrom(typeof(MaterialsController)));
        await using var app = await Start();
        using var client = app.GetTestClient();
        var denied = await client.GetAsync("/context-service/internal/companies?asOf=2026-10-23T08:00:00Z");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("internal_access_denied", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        client.DefaultRequestHeaders.Add("X-Internal-Secret", "synthetic-mvc-secret");
        foreach (var url in new[] { "/context-service/internal/companies", "/context-service/internal/companies?asOf=garbage", "/context-service/internal/changes?after=garbage" })
        {
            using var invalid = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("invalid_request", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        using var malformed = await client.PostAsync("/context-service/internal/material-versions", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("invalid_request", (await malformed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var now = DateTimeOffset.Parse("2026-10-23T08:00:00.0000001Z");
        var request = new MaterialVersionRequest("mvc", "1", "fixture", "news", "Новость", "Original", now, null, null, [], []);
        using var created = await client.PostAsJsonAsync("/context-service/internal/material-versions", request);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var saved = (await created.Content.ReadFromJsonAsync<MaterialVersion>())!;
        Assert.Equal(now.UtcTicks, saved.KnownAt.UtcTicks);
        using var read = await client.GetAsync("/context-service/internal/material-versions/mvc/1");
        Assert.Equal(JsonSerializer.Serialize(saved, ContextJson.Options), JsonSerializer.Serialize(await read.Content.ReadFromJsonAsync<MaterialVersion>(), ContextJson.Options));
        using var conflict = await client.PostAsJsonAsync("/context-service/internal/material-versions", request with
        {
            Text = "Changed"
        });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("This material/version already exists with different content or metadata.", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorMessage").GetString());
        using var missing = await client.GetAsync("/context-service/internal/material-versions/missing/1");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Material version was not found.", (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errorMessage").GetString());
    }
}
