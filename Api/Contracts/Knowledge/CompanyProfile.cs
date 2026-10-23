
namespace ContextService.Contracts.Knowledge;

public sealed record CompanyProfile(Guid CompanyId, string RevisionId, string? BankProfileId,
    string Name, string Description, string[] IndustryCodes, string[] Exposures,
    DateTimeOffset KnownAt, SourceReference[] SourceRefs);
