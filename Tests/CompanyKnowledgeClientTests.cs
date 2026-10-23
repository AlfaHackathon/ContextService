using System.Net;
using System.Text.Json;
using ContextService.Contracts.Knowledge;
using ContextService.Services;
using ContextService.Settings;
using Xunit;

public sealed class CompanyKnowledgeClientTests
{
    [Fact]
    public async Task KnowledgeWriteAndReadUseCompanyServiceContract()
    {
        var profile = new CompanyProfile(Guid.NewGuid(), "r1", null, "Fixture", "Company", [], [], DateTimeOffset.Parse("2026-10-24T08:00:00Z"), []);
        using var fixture = new CompanyKnowledgeFixture(new Handler(profile));
        var saved = await fixture.Client.SaveCompany(profile).RequireContentAsync();
        var listed = await fixture.Client.Companies(profile.KnownAt).RequireContentAsync();
        Assert.Equal(profile.CompanyId, saved.CompanyId);
        Assert.Equal(profile.RevisionId, Assert.Single(listed).RevisionId);
    }

    [Fact]
    public async Task UpstreamFailureDoesNotBecomeEmptyKnowledge()
    {
        using var fixture = new CompanyKnowledgeFixture(new Handler(null));
        var result = await fixture.Client.CompanyVersions();
        Assert.NotNull(result.Error);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.Error.HttpStatusCode);
    }

    private sealed class Handler(CompanyProfile? profile) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.StartsWith("/company-service/internal/knowledge/companies", request.RequestUri!.AbsolutePath);
            var status = profile is null ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            object body = profile is null ? new
            {
                errorMessage = "Company unavailable"
            }
                : request.Method == HttpMethod.Post ? profile : new
                {
                    companies = new[] { profile }
                };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body, MaterialService.Json)) });
        }
    }
}

internal sealed class CompanyKnowledgeFixture : IDisposable
{
    private readonly HttpClient http;
    public CompanyKnowledgeClient Client
    {
        get;
    }
    public CompanyKnowledgeFixture(HttpMessageHandler? handler = null)
    {
        var settings = new CompanyHttpSettings { BaseAddress = "http://company.fixture/" };
        http = new HttpClient(handler ?? new EmptyHandler()) { BaseAddress = new Uri(settings.BaseAddress) };
        Client = new(new RetrievalTransport<CompanyHttpSettings>(new AlfaHackaton.HttpClient.Http.HttpClient<CompanyHttpSettings>(http, settings), settings));
    }
    public void Dispose() => http.Dispose();
    private sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"companies\":[]}") });
    }
}
