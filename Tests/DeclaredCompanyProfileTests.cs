using System.Net;
using System.Text.Json;
using ContextService.Common;
using ContextService.Contracts.Knowledge;
using Xunit;

public sealed class DeclaredCompanyProfileTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-26T10:00:00Z");
    private static readonly SourceReference Snapshot = new("declared-company-fixture", "capture-1", new string('a', 64));
    private static CompanyProfile Profile() => new(Company, "r1", null, "Synthetic company", "Fixture", [], [], At, []);

    [Fact]
    public void LegacyProfileKeepsItsImmutableSerializedShape()
    {
        var profile = Profile();
        var legacy = JsonSerializer.Serialize(new
        {
            profile.CompanyId,
            profile.RevisionId,
            profile.BankProfileId,
            profile.Name,
            profile.Description,
            profile.IndustryCodes,
            profile.Exposures,
            profile.KnownAt,
            profile.SourceRefs
        }, ContextJson.Options);

        var restored = JsonSerializer.Deserialize<CompanyProfile>(legacy, ContextJson.Options)!;

        Assert.Null(restored.DeclaredProfile);
        Assert.Equal(legacy, JsonSerializer.Serialize(restored, ContextJson.Options));
    }

    [Fact]
    public void DeclaredProfilePreservesUnknownsAndExplicitZeroWithoutInventingRevenueUnits()
    {
        var profile = Profile() with
        {
            DeclaredProfile = new(null, null, new("9007199254740993.01", null, null), 0, [Snapshot])
        };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(profile, ContextJson.Options));
        var declared = json.RootElement.GetProperty("declaredProfile");

        Assert.Equal(JsonValueKind.Null, declared.GetProperty("region").ValueKind);
        Assert.Equal(JsonValueKind.Null, declared.GetProperty("industrySections").ValueKind);
        Assert.Equal(0, declared.GetProperty("countOfEmployees").GetInt32());
        var revenue = declared.GetProperty("revenue");
        Assert.Equal("9007199254740993.01", revenue.GetProperty("amount").GetString());
        Assert.Equal(JsonValueKind.Null, revenue.GetProperty("currency").ValueKind);
        Assert.Equal(JsonValueKind.Null, revenue.GetProperty("period").ValueKind);
        var roundTrip = JsonSerializer.Deserialize<CompanyProfile>(json.RootElement.GetRawText(), ContextJson.Options)!;
        Assert.Equal(Snapshot, Assert.Single(roundTrip.DeclaredProfile!.SourceRefs));
    }

    [Fact]
    public async Task KnowledgeProxyKeepsDeclaredSnapshotAndTimeWithoutForwardingUnrelatedFields()
    {
        var profile = Profile() with
        {
            IndustryCodes = ["10.11"],
            DeclaredProfile = new("Fixture region", ["C"], null, null, [Snapshot])
        };
        using var handler = new DeclaredHandler(profile);
        using var fixture = new CompanyKnowledgeFixture(handler);

        var saved = await fixture.Client.SaveCompany(profile).RequireContentAsync();
        var listed = Assert.Single(await fixture.Client.Companies(At).RequireContentAsync());

        Assert.True(handler.WriteObserved);
        Assert.Equal(profile.KnownAt, listed.KnownAt);
        Assert.Equal("10.11", Assert.Single(listed.IndustryCodes));
        Assert.Equal("C", Assert.Single(listed.DeclaredProfile!.IndustrySections!));
        Assert.Equal("Fixture region", saved.DeclaredProfile!.Region);
        Assert.Null(listed.DeclaredProfile.CountOfEmployees);
        Assert.Null(listed.DeclaredProfile.Revenue);
        Assert.Equal(Snapshot, Assert.Single(listed.DeclaredProfile.SourceRefs));
        var forwarded = JsonSerializer.Serialize(listed, ContextJson.Options);
        Assert.DoesNotContain("creatorId", forwarded);
        Assert.DoesNotContain("inn", forwarded);
        Assert.DoesNotContain("email", forwarded);
    }

    private sealed class DeclaredHandler(CompanyProfile profile) : HttpMessageHandler
    {
        public bool WriteObserved { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/company-service/internal/knowledge/companies", request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Post)
            {
                var sent = await request.Content!.ReadAsStringAsync(cancellationToken);
                Assert.Equal(JsonSerializer.Serialize(profile, ContextJson.Options), sent);
                WriteObserved = true;
            }
            else
            {
                Assert.Equal("?asOf=2026-10-26T10%3A00%3A00.0000000%2B00%3A00", request.RequestUri.Query);
            }
            var raw = JsonSerializer.Serialize(profile, ContextJson.Options);
            raw = raw[..^1] + ",\"creatorId\":\"unrelated\",\"inn\":\"unrelated\",\"email\":\"unrelated\"}";
            var body = request.Method == HttpMethod.Post ? raw : "{\"companies\":[" + raw + "]}";
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
