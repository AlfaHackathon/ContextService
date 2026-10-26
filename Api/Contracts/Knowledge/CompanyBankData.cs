namespace ContextService.Contracts.Knowledge;

// Raw owner values: codes, flags and gradations are never interpreted as financial exposure.
public sealed class CompanyBankData
{
    public string? OrgType { get; init; }
    public string? OkfsName { get; init; }
    public string? CompanyAgeYearsGradation { get; init; }
    public string? EcoMarkLabel { get; init; }
    public string? MainOkvedCode { get; init; }
    public string? MainOkvedName { get; init; }
    public string? NegativeMark { get; init; }
    public string? AmountWorkersFnsGradation { get; init; }
    public string? AmountWorkersSparkGradation { get; init; }
    public string? ClType { get; init; }
    public string? ClProductRkoMark { get; init; }
    public string? ClProductGarMark { get; init; }
    public string? ClProductLoaMark { get; init; }
    public string? ClProductLeaMark { get; init; }
    public string? ClProductDepMark { get; init; }
    public string? ClProductZppMark { get; init; }
    public string? FtsFlag { get; init; }
    public string? FtsCountryName { get; init; }
    public string? FactoringMark { get; init; }
    public string? RevenueCompany { get; init; }
    public Guid? GroupId { get; init; }
    public string? City { get; init; }
    public string? Region { get; init; }
}
