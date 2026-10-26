using ContextService.Contracts.Materials;
using ContextService.Services;
using Xunit;

public sealed class DeclaredSourceTests
{
    [Fact]
    public void RawSourceCanBeAdmittedWithoutPretendingToBeCompanyOrMarketEvidence()
    {
        var at=DateTimeOffset.UtcNow;
        var result=MaterialValidation.Normalize(new MaterialVersionRequest("synthetic-csv","v1","company-bank-profile","declared_source",
            "Declared source, no role or employment assertion","{\"headers\":[\"ROLE\"],\"rows\":[[\"\"]]}",at,null,null,[],[],
            new("event_payload",null,at,"full")));
        Assert.Null(result.Error);
        Assert.Equal("declared_source",result.Content!.Kind);
        Assert.Empty(result.Content.CompanyIds);
        Assert.Equal("full",result.Content.Provenance!.TextCompleteness);
    }
}
