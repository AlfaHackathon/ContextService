using System.Net;
using System.Text;
using System.Text.Json;
using ContextService.Common;
using ContextService.Contracts.Materials;
using ContextService.Repositories.Interfaces;
using ContextService.Services;
using Xunit;

public sealed class CompanyMaterialListTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-25T10:00:00.0000001Z");
    private const long Snapshot = 9007199254740993;
    private static CompanyMaterialSummary Summary(string material, string version = "v1", int count = 1) =>
        new(material, version, "synthetic-upload", "company", "Synthetic " + material, At, null, null,
            new string('a', 64), "unknown", count);

    internal sealed class Catalog : ICompanyMaterialListRepository
    {
        public int Calls { get; private set; }
        public Guid CompanyId { get; private set; }
        public DateTimeOffset AsOf { get; private set; }
        public string? Bank { get; private set; }
        public int Limit { get; private set; }
        public CompanyMaterialContinuation? Continuation { get; private set; }
        public CompanyMaterialSummary[] Rows { get; set; } = [];
        public CancellationTokenSource? CancelDuringRead { get; set; }
        public Task<CompanyMaterialSlice> List(Guid companyId, DateTimeOffset asOf, string? bankProfileId,
            int limit, CompanyMaterialContinuation? continuation, CancellationToken ct)
        {
            Calls++;
            CompanyId = companyId;
            AsOf = asOf;
            Bank = bankProfileId;
            Limit = limit;
            Continuation = continuation;
            CancelDuringRead?.Cancel();
            return Task.FromResult(new CompanyMaterialSlice(Snapshot, Rows));
        }
    }

    [Fact]
    public async Task RequiredScopeExplicitTimeLimitsAndBankIdentifiersAreValidatedBeforeReading()
    {
        var catalog = new Catalog();
        var service = new CompanyMaterialListService(catalog);
        var invalid = new[]
        {
            new CompanyMaterialListRequest(), new() { AsOf = "2026-10-25T10:00:00" },
            new() { AsOf = "2026-02-30T10:00:00Z" }, new() { AsOf = "2026-10-25T10:00:00.12345678Z" },
            new() { AsOf = At.ToString("O"), Limit = 0 }, new() { AsOf = At.ToString("O"), Limit = 21 },
            new() { AsOf = At.ToString("O"), BankProfileId = "" }, new() { AsOf = At.ToString("O"), BankProfileId = " U_1" },
            new() { AsOf = At.ToString("O"), BankProfileId = "other/profile" },
            new() { AsOf = At.ToString("O"), BankProfileId = new string('x', 201) }
        };
        foreach (var request in invalid)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await ResultAssert.ErrorAsync(() => service.List(Company, request, CancellationToken.None))).HttpStatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await ResultAssert.ErrorAsync(() => service.List(Guid.Empty,
            new() { AsOf = At.ToString("O") }, CancellationToken.None))).HttpStatusCode);
        Assert.Equal(0, catalog.Calls);
    }

    [Fact]
    public async Task ContinuationPreservesExactTicksWatermarkAndTiePositionWithoutCallingVersionIdsChronology()
    {
        var catalog = new Catalog { Rows = [Summary("a", "a", 2), Summary("a", "z", 2), Summary("b")] };
        var service = new CompanyMaterialListService(catalog);
        var page = await service.List(Company, new() { AsOf = "2026-10-25T13:00:00.0000001+03:00", Limit = 2, BankProfileId = "U_1" }, CancellationToken.None).RequireContentAsync();
        Assert.Equal(Company, page.CompanyId);
        Assert.Equal(At.UtcTicks, page.AsOf.UtcTicks);
        Assert.Equal(TimeSpan.Zero, page.AsOf.Offset);
        Assert.Equal(["a", "z"], page.Items.Select(item => item.VersionId).ToArray());
        Assert.All(page.Items, item => Assert.Equal(2, item.LatestVersionCount));
        Assert.Equal("canonical_changes_only", page.Coverage);
        Assert.NotNull(page.NextCursor);
        catalog.Rows = [Summary("b")];
        var last = await service.List(Company, new() { AsOf = At.ToString("O"), Limit = 20, BankProfileId = "U_1", Cursor = page.NextCursor }, CancellationToken.None).RequireContentAsync();
        Assert.Null(last.NextCursor);
        Assert.Equal("b", Assert.Single(last.Items).MaterialId);
        Assert.Equal(Snapshot, catalog.Continuation!.SnapshotSequence);
        Assert.Equal(At.UtcTicks, catalog.Continuation.After.KnownTicks);
        Assert.Equal("a", catalog.Continuation.After.MaterialId);
        Assert.Equal("z", catalog.Continuation.After.VersionId);
        Assert.Equal("U_1", catalog.Bank);
        Assert.Equal(20, catalog.Limit); // Page size can change without changing the frozen snapshot.
    }

    [Fact]
    public async Task CursorCannotMoveToAnotherCompanyBankOrSnapshotTime()
    {
        var catalog = new Catalog { Rows = [Summary("a"), Summary("b")] };
        var service = new CompanyMaterialListService(catalog);
        var first = await service.List(Company, new() { AsOf = At.ToString("O"), Limit = 1, BankProfileId = "U_1" }, CancellationToken.None).RequireContentAsync();
        foreach (var (company, bank, at) in new[]
        {
            (Guid.Parse("22222222-2222-4222-8222-222222222222"), "U_1", At),
            (Company, "U_2", At), (Company, (string?)null, At), (Company, "U_1", At.AddTicks(1))
        })
        {
            var error = await ResultAssert.ErrorAsync(() => service.List(company,
                new() { AsOf = at.ToString("O"), Cursor = first.NextCursor, BankProfileId = bank }, CancellationToken.None));
            Assert.Equal(HttpStatusCode.BadRequest, error.HttpStatusCode);
        }
        Assert.Equal(1, catalog.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid+base64")]
    [InlineData("bnVsbA")]
    [InlineData("e30")]
    public async Task MalformedOpaqueCursorsAreNotTreatedAsFirstPage(string cursor)
    {
        var catalog = new Catalog();
        var error = await ResultAssert.ErrorAsync(() => new CompanyMaterialListService(catalog).List(Company,
            new() { AsOf = At.ToString("O"), Cursor = cursor }, CancellationToken.None));
        Assert.Equal(HttpStatusCode.BadRequest, error.HttpStatusCode);
        Assert.Equal(0, catalog.Calls);
    }

    [Fact]
    public async Task CursorRejectsUnknownFieldsAndInvalidNumericBoundaries()
    {
        var catalog = new Catalog { Rows = [Summary("a"), Summary("b")] };
        var service = new CompanyMaterialListService(catalog);
        var first = await service.List(Company, new() { AsOf = At.ToString("O"), Limit = 1 }, CancellationToken.None).RequireContentAsync();
        var encoded = first.NextCursor!.Replace('-', '+').Replace('_', '/');
        encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
        var original = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Convert.FromBase64String(encoded))!;
        foreach (var (name, value) in new[]
        {
            ("unexpected", "untrusted"), ("snapshotSequence", "-1"), ("knownTicks", "0"),
            ("knownTicks", At.AddTicks(1).UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("materialId", "bad/path")
        })
        {
            var changed = new Dictionary<string, JsonElement>(original) { [name] = JsonSerializer.SerializeToElement(value) };
            var badCursor = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(changed)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal(HttpStatusCode.BadRequest, (await ResultAssert.ErrorAsync(() => service.List(Company,
                new() { AsOf = At.ToString("O"), Cursor = badCursor }, CancellationToken.None))).HttpStatusCode);
        }
        Assert.Equal(1, catalog.Calls);
    }

    [Fact]
    public async Task EmptyOrShortPageEndsPaginationAndRetainsUnknownProvenance()
    {
        var catalog = new Catalog();
        var service = new CompanyMaterialListService(catalog);
        var empty = await service.List(Company, new() { AsOf = At.ToString("O") }, CancellationToken.None).RequireContentAsync();
        Assert.Empty(empty.Items);
        Assert.Null(empty.NextCursor);
        Assert.Null(catalog.Bank); // Missing bridge grants no bank-scoped access.
        catalog.Rows = [Summary("own")];
        var page = await service.List(Company, new() { AsOf = At.ToString("O") }, CancellationToken.None).RequireContentAsync();
        var summary = Assert.Single(page.Items);
        Assert.Equal("unknown", summary.TextCompleteness);
        Assert.Null(summary.Provenance);
        var json = JsonSerializer.SerializeToElement(summary, ContextJson.Options);
        foreach (var excluded in new[] { "text", "companyIds", "bankProfileIds", "payload", "provenance" })
        {
            Assert.False(json.TryGetProperty(excluded, out _));
        }
        Assert.Equal(new string('a', 64), json.GetProperty("contentHash").GetString());
    }

    [Fact]
    public async Task CancellationBeforeOrAfterTheRepositoryCannotReturnSuccessfulEmptyPage()
    {
        var catalog = new Catalog();
        var service = new CompanyMaterialListService(catalog);
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.List(Company, new() { AsOf = At.ToString("O") }, new CancellationToken(true)));
        Assert.Equal(0, catalog.Calls);
        using var source = new CancellationTokenSource();
        catalog.CancelDuringRead = source;
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.List(Company, new() { AsOf = At.ToString("O") }, source.Token));
    }
}
