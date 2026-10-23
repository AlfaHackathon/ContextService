using AlfaHackaton.HttpClient;
using ContextService.Services;
using ContextService.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Xunit;

public sealed class OrganizationHttpClientTests
{
    [Fact]
    public async Task SharedPackageExecutesLoopbackHttpWithExactJsonHeaderStatusAndCancellation()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        byte[]? received = null;
        string? credential = null;
        app.MapPost("/points/query", async (HttpContext context) =>
        {
            credential = context.Request.Headers["api-key"];
            using var memory = new MemoryStream();
            await context.Request.Body.CopyToAsync(memory);
            received = memory.ToArray();
            return Results.Json(new { result = new { points = Array.Empty<object>() } });
        });
        app.MapGet("/redirect", () => Results.Redirect("/points/query"));
        app.MapGet("/wait", async (CancellationToken ct) => { await Task.Delay(10000, ct); return Results.Ok(); });
        await app.StartAsync();
        try
        {
            var settings = new QdrantHttpSettings
            {
                BaseAddress = app.Urls.Single() + "/",
                AutoRedirect = false,
                DefaultHeaders = headers => headers.Add("api-key", "synthetic-upstream-key")
            };
            var services = new ServiceCollection();
            services.AddAlfaHttpClient(settings);
            services.AddTransient<RetrievalTransport<QdrantHttpSettings>>();
            using var provider = services.BuildServiceProvider();
            var transport = provider.GetRequiredService<RetrievalTransport<QdrantHttpSettings>>();
            var body = new { query = "я", @using = "lexical", limit = 12 };
            using var response = await transport.PostAsJsonAsync("points/query", body, CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("synthetic-upstream-key", credential);
            Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(body, ContextService.Common.ContextJson.Options), received);
            using var payload = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
            Assert.Empty(payload.RootElement.GetProperty("result").GetProperty("points").EnumerateArray());
            using var redirect = await transport.GetAsync("redirect", CancellationToken.None);
            Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.GetAsync("wait", cancel.Token));
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
