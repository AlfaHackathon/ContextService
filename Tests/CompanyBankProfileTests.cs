using System.Net;
using System.Text.Json;
using ContextService.Common;
using ContextService.Contracts.Knowledge;
using ContextService.Services;
using Xunit;

public sealed class CompanyBankProfileTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-26T10:00:00Z");
    internal static CompanyBankProfile Profile(Guid? company = null, string region = "FixtureNorth", string city = "FixtureCity") => new(
        company ?? Guid.Parse("11111111-1111-4111-8111-111111111111"), "capture-1", At,
        new() { Region = region, City = city, MainOkvedCode = "10.11", MainOkvedName = "Synthetic production" },
        "company-bank-profile", [new("synthetic-bank-snapshot", "capture-1", new string('a', 64))],
        new("event_payload", null, At, "full"));

    [Fact]
    public void RawValuesNullsAndExactProvenanceSurviveTypedProjectionWithoutExposureClaims()
    {
        var profile = Profile() with
        {
            BankData = new()
            {
                Region = "FixtureNorth",
                City = "FixtureCity",
                NegativeMark = "0",
                RevenueCompany = "0009007199254740993.0100",
                MainOkvedCode = "10.11",
                AmountWorkersFnsGradation = "raw:100-200",
                FtsCountryName = "Country, raw"
            }
        };
        var before = JsonSerializer.Serialize(profile, ContextJson.Options);
        var text = CompanyBankProfileSearchText.Build(profile);
        Assert.Contains("REGION: FixtureNorth", text);
        Assert.Contains("CITY: FixtureCity", text);
        Assert.Contains("MAIN_OKVED_CODE: 10.11", text);
        Assert.Contains("REVENUE_COMPANY: 0009007199254740993.0100", text);
        Assert.Contains("AMOUNT_WORKERS_FNS_GRADATION: raw:100-200", text);
        Assert.DoesNotContain("RUB", text);
        Assert.DoesNotContain("documented_exposure", text);
        Assert.Equal(before, JsonSerializer.Serialize(profile, ContextJson.Options));
        var restored = JsonSerializer.Deserialize<CompanyBankProfile>(before, ContextJson.Options)!;
        Assert.Equal(Assert.Single(profile.SourceRefs), Assert.Single(restored.SourceRefs));
        Assert.Equal(profile.Provenance, restored.Provenance);
        using var json = JsonDocument.Parse(before);
        var bank = json.RootElement.GetProperty("bankData");
        Assert.Equal(23, bank.EnumerateObject().Count());
        Assert.Equal("0", bank.GetProperty("negativeMark").GetString());
        Assert.Equal(JsonValueKind.Null, bank.GetProperty("orgType").ValueKind);
        Assert.False(json.RootElement.TryGetProperty("name", out _));
        Assert.False(json.RootElement.TryGetProperty("creatorId", out _));
        Assert.False(bank.TryGetProperty("industrySections", out _));
    }

    [Fact]
    public async Task BankVersionsFollowOpaquePagesAndKeepAllImmutableRevisions()
    {
        var calls = 0;
        var profile = Profile();
        var corrected = profile with
        {
            RevisionId = "capture-2",
            KnownAt = At.AddSeconds(1),
            BankData = new() { Region = "FixtureSouth" },
            Provenance = profile.Provenance with { RetrievedAt = At.AddSeconds(1) }
        };
        using var fixture = new CompanyKnowledgeFixture(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/company-service/internal/bank-profiles/versions", request.RequestUri!.AbsolutePath);
            calls++;
            Assert.Equal(calls == 1 ? "?limit=100" : "?limit=100&after=9007199254740993", request.RequestUri.Query);
            return new CompanyBankProfilePage([calls == 1 ? profile : corrected], calls == 1 ? "9007199254740993" : null);
        }));
        var result = await fixture.Client.BankProfileVersions().RequireContentAsync();
        Assert.Equal(2, calls);
        Assert.Equal(new[] { "capture-1", "capture-2" }, result.Select(p => p.RevisionId));
    }

    [Fact]
    public async Task RepeatedCursorFailsRatherThanPublishingAPartialProjection()
    {
        var calls = 0;
        using var fixture = new CompanyKnowledgeFixture(new Handler(_ =>
        {
            calls++;
            return new CompanyBankProfilePage([Profile()], "9");
        }));
        var result = await fixture.Client.BankProfileVersions();
        Assert.NotNull(result.Error);
        Assert.Equal(HttpStatusCode.BadGateway, result.Error.HttpStatusCode);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task FutureOwnerRevisionAndMalformedSnapshotDoNotBecomeCurrentProfileData()
    {
        foreach (var invalid in new[]
        {
            Profile() with { KnownAt = At.AddSeconds(1), Provenance = new("event_payload", null, At.AddSeconds(1), "full") },
            Profile() with { SourceId = "documentary-fixture" },
            Profile() with { SourceRefs = [] }
        })
        {
            using var fixture = new CompanyKnowledgeFixture(new Handler(_ => invalid));
            var result = await fixture.Client.BankProfile(invalid.CompanyId, At);
            Assert.NotNull(result.Error);
            Assert.Equal(HttpStatusCode.BadGateway, result.Error.HttpStatusCode);
        }
    }

    [Fact]
    public async Task BankReadUsesExplicitHistoricalTimeAndNeverRequestsOperationalCompanyFields()
    {
        var profile = Profile();
        using var fixture = new CompanyKnowledgeFixture(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/company-service/internal/bank-profiles/{profile.CompanyId:D}", request.RequestUri!.AbsolutePath);
            Assert.Equal("?asOf=2026-10-26T10%3A00%3A00.0000000%2B00%3A00", request.RequestUri.Query);
            return profile;
        }));
        var read = await fixture.Client.BankProfile(profile.CompanyId, At).RequireContentAsync();
        Assert.Equal(profile.CompanyId, read.CompanyId);
        Assert.Equal(profile.KnownAt, read.KnownAt);
        Assert.Equal("FixtureNorth", read.BankData.Region);
    }

    private sealed class Handler(Func<HttpRequestMessage, object> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(response(request), ContextJson.Options))
            });
    }
}
