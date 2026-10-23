using ContextService.Contracts.Materials;
using ContextService.Services;
using Xunit;

public sealed class ContextBudgetTests
{
    private static MaterialVersion Material(string id, string version, string text, string kind = "market") => new(id, version, "fixture", kind, id, text, DateTimeOffset.Parse("2026-09-11T10:30:00Z"), null, null, [], [], new string('a', 64));
    [Fact]
    public void PacksWholeOriginalsAndNeverSeparatesAHitFromItsCorrections()
    {
        var news = Material("event", "1", "News", "news");
        var profile = Material("profile", "1", "Required company profile", "company");
        var old = Material("terms", "1", new string('x', 2500));
        var correction = Material("terms", "2", new string('y', 2500));
        var small = Material("other", "1", "A smaller complete original");
        var packed = (ContextBudget.Pack(news, [profile, old, correction, small], new HashSet<string> { "profile" }, new Dictionary<string, double> { ["terms"] = 10, ["other"] = 1 }, 4000)).RequireContent();
        Assert.Equal(new[] { "other", "profile" }, packed.Materials.Select(m => m.MaterialId).Order());
        Assert.Equal(small.Text, packed.Materials.Single(m => m.MaterialId == "other").Text);
        Assert.Equal(new[] { "1", "2" }, packed.Budget.OmittedSources.Select(s => s.VersionId).Order());
        Assert.Equal("context_budget", packed.Budget.StopReason);
        Assert.True(packed.Budget.UsedCharacters <= packed.Budget.MaximumCharacters);
    }

    [Fact]
    public void MandatoryOverflowDoesNotPretendACompleteProfileWasDelivered()
    {
        var news = Material("event", "1", "News", "news");
        var profile = Material("profile", "1", new string('x', 6000), "company");
        var error = ResultAssert.Error(() => ContextBudget.Pack(news, [profile], new HashSet<string> { "profile" }, new Dictionary<string, double>(), 4000));
        Assert.Equal("mandatory_context_budget_exceeded", error.Code);
    }
}
