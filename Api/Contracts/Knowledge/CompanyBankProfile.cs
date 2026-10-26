using ContextService.Contracts.Materials;

namespace ContextService.Contracts.Knowledge;

// CompanyService owns this immutable declaration. It is not a documentary exposure profile.
public sealed record CompanyBankProfile(Guid CompanyId, string RevisionId, DateTimeOffset KnownAt,
    CompanyBankData BankData, string SourceId, SourceReference[] SourceRefs, MaterialProvenance Provenance);

public sealed record CompanyBankProfilePage(CompanyBankProfile[] Profiles, string? NextCursor);
